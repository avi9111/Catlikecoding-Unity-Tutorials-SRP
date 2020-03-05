using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    CullingResults _cullingResults;

    Material _errorMaterial;

    CommandBuffer cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };

    private DrawingSettings _drawingSettings;
    private bool _dynamicBatching;
    private bool _instancing;
    public MyPipeline(bool dynamicBatching, bool instancing)
    {
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

        context.Submit();
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
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

        cameraBuffer.BeginSample("Render Camera");
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();
    
        SortingSettings sortingSettings = new SortingSettings();
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

        cameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        EndCameraRendering(context, camera);
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
        
        DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera){criteria = SortingCriteria.CommonOpaque});
     
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