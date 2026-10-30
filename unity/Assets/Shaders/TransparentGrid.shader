Shader "Inspection/TransparentGrid"
{
    Properties
    {
        [MainColor] _BaseColor("Line Color", Color) = (.05, .9, 1, .1)
        _GridSpacing("Grid Spacing (meters)", Float) = .1
        _LineWidth("Line Width (meters)", Float) = .002
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _GridSpacing, _LineWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float spacing = max(_GridSpacing, .0001);
                float2 distanceToLine = abs(frac(input.positionWS.xz / spacing + .5) - .5) * spacing;
                float2 aa = max(fwidth(input.positionWS.xz), .000001);
                float2 gridLine = 1 - smoothstep(_LineWidth * .5, _LineWidth * .5 + aa, distanceToLine);
                float coverage = max(gridLine.x, gridLine.y);
                clip(coverage - .005);
                return half4(_BaseColor.rgb, _BaseColor.a * coverage);
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            fixed4 _BaseColor;
            float _GridSpacing, _LineWidth;
            struct Varyings { float4 position : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(float4 position : POSITION)
            {
                Varyings output;
                output.positionWS = mul(unity_ObjectToWorld, position).xyz;
                output.position = UnityObjectToClipPos(position);
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                float spacing = max(_GridSpacing, .0001);
                float2 distanceToLine = abs(frac(input.positionWS.xz / spacing + .5) - .5) * spacing;
                float2 aa = max(fwidth(input.positionWS.xz), .000001);
                float2 gridLine = 1 - smoothstep(_LineWidth * .5, _LineWidth * .5 + aa, distanceToLine);
                float coverage = max(gridLine.x, gridLine.y);
                clip(coverage - .005);
                return fixed4(_BaseColor.rgb, _BaseColor.a * coverage);
            }
            ENDCG
        }
    }
    Fallback Off
}
