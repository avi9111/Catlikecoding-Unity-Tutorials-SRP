using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipeline : RenderPipeline
{
    CullingResults cullingResults;
    Material errorMaterial;

    CommandBuffer commandBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };

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
        ScriptableCullingParameters cullingParameters;
        if (!camera.TryGetCullingParameters(out cullingParameters))
        {
            return;
        }

        BeginCameraRendering(context, camera);
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif
        context.SetupCameraProperties(camera);
        commandBuffer.BeginSample("Render Camera commandBuffer");
        CameraClearFlags clearFlags = camera.clearFlags;
        commandBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);
        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();

        cullingResults = context.Cull(ref cullingParameters);
        SortingSettings sortingSettings = new SortingSettings(camera);
        sortingSettings.criteria = SortingCriteria.CommonOpaque;
        DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSettings);
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;
        filteringSettings.renderQueueRange = RenderQueueRange.opaque;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        context.DrawSkybox(camera);

        sortingSettings.criteria = SortingCriteria.CommonTransparent;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);

        DrawDefaultPipeline(context, camera);

        commandBuffer.EndSample("Render Camera commandBuffer");
        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();

        EndCameraRendering(context, camera);
    }

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errorShader)
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
        drawingSettings.overrideMaterial = errorMaterial;
        drawingSettings.overrideMaterialPassIndex = 0;
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;
        filteringSettings.renderQueueRange = RenderQueueRange.opaque;

        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }
}