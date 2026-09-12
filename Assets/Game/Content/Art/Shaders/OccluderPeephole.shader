// 遮挡透视孔着色器：在遮挡物上以角色为中心「抠」出一个圆形孔，孔内完全透明。
//
// 为什么需要它：本项目的相机是固定俯角（62 度）的斜俯视相机。玩家走到土墙、
// 集装箱或厂房后面时，相机与角色之间会被遮挡物挡住；常见的做法是把相机拉近，
// 但那样会改变玩家已经熟悉的构图与视野。这里改成在遮挡物上开一个跟着角色走的圆孔，
// 相机距离与俯角保持不变——玩家仍然看得到自己的角色，也不丢失空间感。
//
// 三个关键点：
//   1. 圆孔在屏幕像素空间里判定，因此永远是正圆，不会随窗口宽高比被拉成椭圆；
//   2. 只有「确实挡在角色前面」的像素才开孔（比较片元的视深度与角色的视深度），
//      否则角色背后的墙也会莫名其妙出现一个洞；
//   3. 圆孔参数由 OcclusionPeepholeController 每帧通过全局变量写入，
//      因此全场景共用一份数据，不需要逐个材质设置。
Shader "RaidDemo/OccluderPeephole"
{
    Properties
    {
        _BaseMap("基础贴图", 2D) = "white" {}
        _BaseColor("基础色", Color) = (1, 1, 1, 1)
        _Smoothness("光滑度", Range(0, 1)) = 0.1
        _Cutoff("阴影剔除阈值", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // ---------------------------------------------------------------- 前向光照
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half _Cutoff;
            CBUFFER_END

            // 圆孔参数（全局）：xy = 屏幕像素中心，z = 角色的视深度，w = 半径（像素）。
            // w 小于等于 0 表示本帧不开孔（例如角色不在镜头里）。
            float4 _PeepholeParams;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 透视孔：屏幕像素空间判定，保证正圆；深度比较保证只对挡在角色前面的像素生效。
                if (_PeepholeParams.w > 0.0h)
                {
                    float2 screenPixel = (input.screenPos.xy / input.screenPos.w) * _ScreenParams.xy;
                    float fragmentDepth = -TransformWorldToView(input.positionWS).z;
                    if (distance(screenPixel, _PeepholeParams.xy) < _PeepholeParams.w
                        && fragmentDepth < _PeepholeParams.z - 0.05h)
                    {
                        discard;
                    }
                }

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.fogCoord = 0.0h;

                return UniversalFragmentBlinnPhong(inputData, surfaceData);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- 阴影投射
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ---------------------------------------------------------------- 深度
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
