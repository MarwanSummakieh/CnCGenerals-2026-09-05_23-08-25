Shader "NeonFrontier/Battlefield Water"
{
    Properties
    {
        _BaseMap("Silt and current texture", 2D) = "white" {}
        _BaseColor("Water tint", Color) = (1,1,1,1)
        _Smoothness("Surface smoothness", Range(0,1)) = 0.79
        _Metallic("Reflection strength", Range(0,1)) = 0.24
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Flowing river"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz;
                float time = _Time.y;
                float longWave = sin(p.x * 1.8 + p.y * .27 + time * .7);
                float crossWave = cos(p.y * 2.7 - p.x * .65 - time * 1.25);
                float shortWave = sin(p.x * 5.1 + p.y * 1.6 + time * 1.6);
                half3 normalWS = normalize(half3(longWave * .08 + shortWave * .022, 1,
                    crossWave * .042 + longWave * .022));
                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sunlight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 diffuse = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    input.uv + float2(longWave, crossWave) * .004).rgb * _BaseColor.rgb;
                half fresnel = pow(1 - saturate(dot(normalWS, viewDirection)), 4);
                half3 reflectedSky = half3(.39, .46, .44);
                half lighting = .72 + saturate(dot(normalWS, sunlight.direction)) * .28;
                half3 color = diffuse * lighting;
                color = lerp(color, reflectedSky, fresnel * (.25 + _Metallic));
                half3 halfway = SafeNormalize(sunlight.direction + viewDirection);
                half specular = pow(saturate(dot(normalWS, halfway)), lerp(48, 196, _Smoothness));
                color += sunlight.color * specular * .5 * sunlight.shadowAttenuation;
                color *= lerp(.69, 1, sunlight.shadowAttenuation);
                // Match the exact navigation river curve; a faint shoreline replaces neon foam.
                float center = sin(p.y * .024) * 6 + sin(p.y * .051 + .5) * 3.5;
                half shore = 1 - smoothstep(.05, 1.25, 11 - abs(p.x - center));
                color = lerp(color, half3(.29, .32, .24), shore * .34);
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
