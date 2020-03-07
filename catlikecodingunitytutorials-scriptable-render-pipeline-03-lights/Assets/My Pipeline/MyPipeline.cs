using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    const int maxVisibleLights = 4;

    private static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    private static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
    private static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    private static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
    Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

    CullingResults _cullingResults;

    Material _errorMaterial;

    CommandBuffer commandBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };

    private bool _dynamicBatching;
    private bool _instancing;

    public MyPipeline(bool dynamicBatching, bool instancing)
    {
        GraphicsSettings.lightsUseLinearIntensity = true;
        _dynamicBatching = dynamicBatching;
        _instancing = instancing;
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        BeginFrameRendering(context, cameras);

        foreach (var camera in cameras)
        {
            Render(context, camera);
        }
        
        EndFrameRendering(context, cameras);
    }

    void Render(ScriptableRenderContext context, Camera camera)
    {
        ScriptableCullingParameters scriptableCullingParameters;
        if (!camera.TryGetCullingParameters(out scriptableCullingParameters))
        {
            return;
        }

#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        BeginCameraRendering(context, camera);
        _cullingResults = context.Cull(ref scriptableCullingParameters);
        context.SetupCameraProperties(camera);

        CameraClearFlags clearFlags = camera.clearFlags;
        commandBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

        ConfigureLights();

        commandBuffer.BeginSample("Render Camera");
        commandBuffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
        commandBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions);
        commandBuffer.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
        commandBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);
        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();

        SortingSettings sortingSettings = new SortingSettings(camera);
        sortingSettings.criteria = SortingCriteria.CommonOpaque;
        DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSettings);
        drawingSettings.enableInstancing = _instancing;
        drawingSettings.enableDynamicBatching = _dynamicBatching;

        FilteringSettings filteringSettings = FilteringSettings.defaultValue;
        filteringSettings.renderQueueRange = RenderQueueRange.opaque;
        context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);

        context.DrawSkybox(camera);

        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);

        DrawDefaultPipeline(context, camera);

        commandBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();
        
        context.Submit();
        EndCameraRendering(context, camera);
    }

    void ConfigureLights()
    {
        for (int i = 0; i < _cullingResults.visibleLights.Length; i++)
        {
            if (i >= maxVisibleLights)
            {
                return;
            }
            
            
            VisibleLight light = _cullingResults.visibleLights[i];
            
            visibleLightColors[i] = light.finalColor;
            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;
            
            if (light.lightType == LightType.Directional)
            {
                Vector4 v = light.localToWorldMatrix.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visibleLightDirectionsOrPositions[i] = v;
            }
            else
            {
                visibleLightDirectionsOrPositions[i] = light.localToWorldMatrix.GetColumn(3);
                attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
                if (light.lightType == LightType.Spot)
                {
                    Vector4 v = light.localToWorldMatrix.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightSpotDirections[i] = v;
                
                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);
                    float innerCos = Mathf.Cos(Mathf.Atan((64f - 18f) / 64f * outerTan));
                    float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;
                }
            }

            visibleLightAttenuations[i] = attenuation;
        }
    }

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (_errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            _errorMaterial = new Material(errorShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        SortingSettings sortingSettings = new SortingSettings(camera);
        sortingSettings.criteria = SortingCriteria.CommonOpaque;
        DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), sortingSettings);

        drawingSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
        drawingSettings.SetShaderPassName(2, new ShaderTagId("Always"));
        drawingSettings.SetShaderPassName(3, new ShaderTagId("Vertex"));
        drawingSettings.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
        drawingSettings.SetShaderPassName(5, new ShaderTagId("VertexLM"));
        drawingSettings.overrideMaterial = _errorMaterial;
        drawingSettings.overrideMaterialPassIndex = 0;

        FilteringSettings filteringSettings = FilteringSettings.defaultValue;
        filteringSettings.renderQueueRange = RenderQueueRange.opaque;

        context.DrawRenderers(_cullingResults, ref drawingSettings, ref filteringSettings);
    }
}