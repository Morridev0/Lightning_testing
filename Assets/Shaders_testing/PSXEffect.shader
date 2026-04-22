Shader "Custom/PSXCameraEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Resolution and Pixelation)]
        _PixelSize ("Pixel Size", Range(1, 16)) = 2

        [Header(Color Depth)]
        _ColorDepth ("Color Depth (bits per channel)", Range(1, 8)) = 4

        [Header(Dithering)]
        _DitherStrength ("Dither Strength", Range(0, 1)) = 0.4
        _DitherScale ("Dither Pattern Scale", Range(1, 8)) = 1

        [Header(Film Grain)]
        _GrainStrength ("Grain Strength", Range(0, 1)) = 0.15
        _GrainSize ("Grain Size", Range(0.5, 4)) = 1.0
        _GrainSpeed ("Grain Animation Speed", Range(0, 10)) = 3.0

        [Header(Scanlines)]
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.15
        _ScanlineFrequency ("Scanline Frequency", Range(100, 1000)) = 240

        [Header(Chromatic Aberration)]
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.02)) = 0.003

        [Header(Vignette)]
        _VignetteStrength ("Vignette Strength", Range(0, 2)) = 0.4
        _VignetteRadius ("Vignette Radius", Range(0.1, 1)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZWrite Off
        Cull Off

        Pass
        {
            Name "PSXEffect"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;

                float  _PixelSize;
                float  _ColorDepth;

                float  _DitherStrength;
                float  _DitherScale;

                float  _GrainStrength;
                float  _GrainSize;
                float  _GrainSpeed;

                float  _ScanlineStrength;
                float  _ScanlineFrequency;

                float  _ChromaticAberration;

                float  _VignetteStrength;
                float  _VignetteRadius;
            CBUFFER_END

            // -------------------------------------------------------
            // Bayer 8x8 dithering matrix (normalized 0..1)
            // -------------------------------------------------------
            float BayerMatrix8(int2 pos)
            {
                static const float bayer[64] = {
                     0, 32,  8, 40,  2, 34, 10, 42,
                    48, 16, 56, 24, 50, 18, 58, 26,
                    12, 44,  4, 36, 14, 46,  6, 38,
                    60, 28, 52, 20, 62, 30, 54, 22,
                     3, 35, 11, 43,  1, 33,  9, 41,
                    51, 19, 59, 27, 49, 17, 57, 25,
                    15, 47,  7, 39, 13, 45,  5, 37,
                    63, 31, 55, 23, 61, 29, 53, 21
                };
                int idx = (pos.y % 8) * 8 + (pos.x % 8);
                return bayer[idx] / 63.0;
            }

            // -------------------------------------------------------
            // Pseudo-random hash for film grain
            // -------------------------------------------------------
            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            // -------------------------------------------------------
            // Vertex shader
            // -------------------------------------------------------
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            // -------------------------------------------------------
            // Fragment shader
            // -------------------------------------------------------
            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // --------------------------------------------------
                // 1. PIXEL SNAP (retro resolution reduction)
                // --------------------------------------------------
                float2 resolution = _MainTex_TexelSize.zw; // screen size in pixels
                float2 pixelUV = floor(uv * resolution / _PixelSize) * _PixelSize / resolution;

                // --------------------------------------------------
                // 2. CHROMATIC ABERRATION
                // --------------------------------------------------
                float2 caOffset = (pixelUV - 0.5) * _ChromaticAberration;
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pixelUV + caOffset).r;
                half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pixelUV).g;
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pixelUV - caOffset).b;
                half4 col = half4(r, g, b, 1.0);

                // --------------------------------------------------
                // 3. DITHERING (Bayer 8x8)
                // --------------------------------------------------
                // Pixel position in screen space for the Bayer lookup
                int2 screenPos = int2(pixelUV * resolution / _PixelSize) * int(_DitherScale);
                float ditherValue = BayerMatrix8(screenPos) - 0.5; // range -0.5..0.5

                // Color levels from bit depth
                float levels = pow(2.0, _ColorDepth) - 1.0;

                // Apply dither offset before quantising
                half3 dithered = col.rgb + ditherValue * _DitherStrength * (1.0 / levels);

                // --------------------------------------------------
                // 4. COLOR DEPTH REDUCTION (posterisation)
                // --------------------------------------------------
                half3 quantised = floor(dithered * levels + 0.5) / levels;
                col.rgb = quantised;

                // --------------------------------------------------
                // 5. FILM GRAIN
                // --------------------------------------------------
                // Animate grain every frame using _Time
                float2 grainUV = pixelUV * resolution / (8.0 * _GrainSize);
                grainUV += float2(_Time.y * _GrainSpeed * 0.1, _Time.y * _GrainSpeed * 0.07);
                float grain = Hash21(grainUV) * 2.0 - 1.0; // -1..1
                col.rgb += grain * _GrainStrength;

                // --------------------------------------------------
                // 6. SCANLINES
                // --------------------------------------------------
                float scanline = sin(pixelUV.y * _ScanlineFrequency * 3.14159) * 0.5 + 0.5;
                // Subtle darkening on alternate lines
                scanline = lerp(1.0, scanline, _ScanlineStrength);
                col.rgb *= scanline;

                // --------------------------------------------------
                // 7. VIGNETTE
                // --------------------------------------------------
                float2 vigUV = uv - 0.5;
                float vignette = smoothstep(_VignetteRadius, _VignetteRadius - 0.3, length(vigUV));
                col.rgb *= lerp(1.0, vignette, _VignetteStrength);

                // --------------------------------------------------
                // Final clamp
                // --------------------------------------------------
                col.rgb = saturate(col.rgb);
                col.a = 1.0;

                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
