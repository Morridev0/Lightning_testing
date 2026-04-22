using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

// ================================================================
//  PSXVolumeComponent
//  En tu Global Volume: Add Override -> Custom -> PSX Camera Effect
// ================================================================
[System.Serializable, VolumeComponentMenu("Custom/PSX Camera Effect")]
public class PSXVolumeComponent : VolumeComponent, IPostProcessComponent
{
    [Header("Resolution and Pixelation")]
    public ClampedFloatParameter pixelSize = new ClampedFloatParameter(2f, 1f, 16f);

    [Header("Color Depth")]
    public ClampedFloatParameter colorDepth = new ClampedFloatParameter(4f, 1f, 8f);

    [Header("Dithering")]
    public ClampedFloatParameter ditherStrength = new ClampedFloatParameter(0.4f, 0f, 1f);
    public ClampedFloatParameter ditherScale    = new ClampedFloatParameter(1f,   1f, 8f);

    [Header("Film Grain")]
    public ClampedFloatParameter grainStrength = new ClampedFloatParameter(0.15f, 0f,   1f);
    public ClampedFloatParameter grainSize     = new ClampedFloatParameter(1f,    0.5f, 4f);
    public ClampedFloatParameter grainSpeed    = new ClampedFloatParameter(3f,    0f,   10f);

    [Header("Scanlines")]
    public ClampedFloatParameter scanlineStrength  = new ClampedFloatParameter(0.15f, 0f,   1f);
    public ClampedFloatParameter scanlineFrequency = new ClampedFloatParameter(240f,  100f, 1000f);

    [Header("Chromatic Aberration")]
    public ClampedFloatParameter chromaticAberration = new ClampedFloatParameter(0.003f, 0f, 0.02f);

    [Header("Vignette")]
    public ClampedFloatParameter vignetteStrength = new ClampedFloatParameter(0.4f,  0f,   2f);
    public ClampedFloatParameter vignetteRadius   = new ClampedFloatParameter(0.75f, 0.1f, 1f);

    public bool IsActive()         => active;
    public bool IsTileCompatible() => false;
}

// ================================================================
//  PSXRendererFeature
//  Universal Renderer Data -> Add Renderer Feature
// ================================================================
public class PSXRendererFeature : ScriptableRendererFeature
{
    public Shader psxShader;

    private PSXRenderPass _pass;
    private Material      _material;

    public override void Create()
    {
        if (psxShader != null)
            _material = CoreUtils.CreateEngineMaterial(psxShader);

        _pass = new PSXRenderPass(_material)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Debug.Log("PSX: AddRenderPasses llamado");
        if (_material == null)
        {
            Debug.LogWarning("PSXRendererFeature: shader no asignado o material no creado.");
            return;
        }

        PSXVolumeComponent comp = VolumeManager.instance.stack.GetComponent<PSXVolumeComponent>();
        if (comp == null || !comp.IsActive()) return;

        _pass.UpdateMaterial(comp);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
    }
}

// ================================================================
//  PSXRenderPass  —  RenderGraph API (Unity 6 / URP 17+)
// ================================================================
public class PSXRenderPass : ScriptableRenderPass
{
    private Material _material;

    // Shader property IDs
    private static readonly int ID_PixelSize      = Shader.PropertyToID("_PixelSize");
    private static readonly int ID_ColorDepth     = Shader.PropertyToID("_ColorDepth");
    private static readonly int ID_DitherStrength = Shader.PropertyToID("_DitherStrength");
    private static readonly int ID_DitherScale    = Shader.PropertyToID("_DitherScale");
    private static readonly int ID_GrainStrength  = Shader.PropertyToID("_GrainStrength");
    private static readonly int ID_GrainSize      = Shader.PropertyToID("_GrainSize");
    private static readonly int ID_GrainSpeed     = Shader.PropertyToID("_GrainSpeed");
    private static readonly int ID_ScanlineStr    = Shader.PropertyToID("_ScanlineStrength");
    private static readonly int ID_ScanlineFreq   = Shader.PropertyToID("_ScanlineFrequency");
    private static readonly int ID_Chroma         = Shader.PropertyToID("_ChromaticAberration");
    private static readonly int ID_VigStr         = Shader.PropertyToID("_VignetteStrength");
    private static readonly int ID_VigRadius      = Shader.PropertyToID("_VignetteRadius");

    private class PassData
    {
        public Material      material;
        public TextureHandle source;
        public TextureHandle destination;
    }

    public PSXRenderPass(Material material)
    {
        _material = material;
        requiresIntermediateTexture = true;
    }

    public void UpdateMaterial(PSXVolumeComponent comp)
    {
        if (_material == null) return;
        _material.SetFloat(ID_PixelSize,      comp.pixelSize.value);
        _material.SetFloat(ID_ColorDepth,     comp.colorDepth.value);
        _material.SetFloat(ID_DitherStrength, comp.ditherStrength.value);
        _material.SetFloat(ID_DitherScale,    comp.ditherScale.value);
        _material.SetFloat(ID_GrainStrength,  comp.grainStrength.value);
        _material.SetFloat(ID_GrainSize,      comp.grainSize.value);
        _material.SetFloat(ID_GrainSpeed,     comp.grainSpeed.value);
        _material.SetFloat(ID_ScanlineStr,    comp.scanlineStrength.value);
        _material.SetFloat(ID_ScanlineFreq,   comp.scanlineFrequency.value);
        _material.SetFloat(ID_Chroma,         comp.chromaticAberration.value);
        _material.SetFloat(ID_VigStr,         comp.vignetteStrength.value);
        _material.SetFloat(ID_VigRadius,      comp.vignetteRadius.value);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        if (resourceData.isActiveTargetBackBuffer) return;

        TextureHandle source = resourceData.activeColorTexture;

        // Usamos graphicsFormat directamente para evitar el cast RenderTextureFormat
        TextureDesc srcDesc = renderGraph.GetTextureDesc(source);
        RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(srcDesc.width, srcDesc.height)
        {
            graphicsFormat  = srcDesc.colorFormat,
            depthBufferBits = 0,
            msaaSamples     = 1,
            sRGB            = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(srcDesc.colorFormat),
            dimension       = TextureDimension.Tex2D
        };

        TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph,
            rtDesc,
            "_PSXTemp",
            false
        );

        // Pass 1: aplica el efecto PSX de source a destination
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Effect", out var passData))
        {
            passData.material    = _material;
            passData.source      = source;
            passData.destination = destination;

            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
            });
        }

        // Pass 2: copia el resultado de vuelta al color activo
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("PSX Copy Back", out var passData))
        {
            passData.material    = null;
            passData.source      = destination;
            passData.destination = source;

            builder.UseTexture(destination);
            builder.SetRenderAttachment(source, 0);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }
}
