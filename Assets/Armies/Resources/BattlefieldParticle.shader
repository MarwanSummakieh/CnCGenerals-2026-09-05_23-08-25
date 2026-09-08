Shader "NeonFrontier/BattlefieldParticle"
{
    Properties
    {
        _MainTex ("Particle", 2D) = "white" {}
        _SrcBlend ("Source blend", Float) = 5
        _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            Varyings vert(Attributes i)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; o.color = i.color; return o;
            }
            half4 frag(Varyings i) : SV_Target { return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color; }
            ENDHLSL
        }
    }
}
