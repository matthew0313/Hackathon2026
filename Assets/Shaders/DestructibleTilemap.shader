// Lit tilemap shader that multiplies a per-instance damage mask into the tiles' alpha, with an
// optional rim on the carved edges.
//
// Identical in structure to Custom/Destructible Sprite (see DestructibleSprite.shader), with one
// difference: a tilemap draws from many sprite-atlas rects at once, so the mask cannot be found by
// remapping the texture UV the way the sprite shader does. Instead each vertex carries its local
// (object-space) position through to the fragment, and _MaskRect describes the mask's footprint in
// that local space (xy = origin, zw = size) -- the remap in ApplyDamage is the same maths either way.
//
// The mask itself is fed per-renderer via MaterialPropertyBlock by DestructibleTilemap.cs.
Shader "Custom/Destructible Tilemap"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [NoScaleOffset] _DamageMask("Damage Mask", 2D) = "white" {}
        // xy = mask origin in local space, zw = its local-space size. Set from script.
        _MaskRect("Mask Rect", Vector) = (0,0,1,1)
        // xy = 1/maskSize, zw = maskSize. Set from script.
        _MaskTexelSize("Mask Texel Size", Vector) = (1,1,1,1)
        _RimColor("Rim Color", Color) = (1, 0.45, 0.15, 1)
        _RimTexels("Rim Width (mask texels)", Range(0, 8)) = 1.5

        // Legacy properties, so materials using this shader can fall back to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
                float2 localPos    : TEXCOORD7;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MaskRect;
                float4 _MaskTexelSize;
                half4 _RimColor;
                half _RimTexels;
            CBUFFER_END

            #include "DestructibleMask.hlsl"

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                o.localPos = input.positionOS.xy;

                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                // CommonLitFragment, but with the damage mask folded into alpha before lighting.
                half4 main = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main = ApplyDamage(main, input.localPos);

                const half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                SurfaceData2D surfaceData;
                InputData2D inputData;

                InitializeSurfaceData(main.rgb, main.a, mask, normalTS, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);

            #if defined(DEBUG_DISPLAY)
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, input.positionWS, input.positionCS, _MainTex);
                surfaceData.normalWS = input.normalWS;
            #endif

                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
                float2  localPos        : TEXCOORD7;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                float4 _MaskRect;
                float4 _MaskTexelSize;
                half4 _RimColor;
                half _RimTexels;
            CBUFFER_END

            #include "DestructibleMask.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                o.localPos = input.positionOS.xy;

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                // Carved texels must not write normals, or normal-mapped lights would shade the holes.
                const half4 mainTex = ApplyDamage(input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv), input.localPos);
                const half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                return NormalsRenderingShared(mainTex, normalTS, input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
                float2 localPos : TEXCOORD7;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MaskRect;
                float4 _MaskTexelSize;
                half4 _RimColor;
                half _RimTexels;
            CBUFFER_END

            #include "DestructibleMask.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
                o.localPos = input.positionOS.xy;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                return ApplyDamage(CommonUnlitFragment(input, input.color), input.localPos);
            }
            ENDHLSL
        }
    }
}
