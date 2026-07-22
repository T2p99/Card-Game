Shader "Hidden/CardGame/RotateCard90"
{
    Properties
    {
        _MainTex ("Card", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                return tex2D(_MainTex, float2(input.uv.y, 1.0 - input.uv.x));
            }
            ENDCG
        }
    }

    Fallback Off
}
