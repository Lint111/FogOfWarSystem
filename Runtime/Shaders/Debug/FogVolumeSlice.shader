Shader "FogOfWar/Debug/FogVolumeSlice"
{
    Properties
    {
        _FogVolume ("Fog Volume", 3D) = "white" {}
        _SliceHeight ("Slice Height (0-1)", Range(0, 1)) = 0.5
        _VisibleColor ("Visible Color", Color) = (0.2, 0.8, 0.2, 0.4)
        _HiddenColor ("Hidden Color", Color) = (0.3, 0.3, 0.3, 0.4)
        _VolumeMin ("Volume Min", Vector) = (-50, -10, -50, 0)
        _VolumeMax ("Volume Max", Vector) = (50, 40, 50, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            Texture3D<float> _FogVolume;
            SamplerState sampler_FogVolume;

            float _SliceHeight;
            float4 _VisibleColor;
            float4 _HiddenColor;
            float4 _VolumeMin;
            float4 _VolumeMax;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Calculate UVW for 3D texture sampling
                float3 uvw = float3(i.uv.x, _SliceHeight, i.uv.y);

                // Sample fog volume
                float visibility = _FogVolume.SampleLevel(sampler_FogVolume, uvw, 0);

                // Compute gradient for pseudo-3D shading
                float eps = 1.0 / 128.0;
                float3 gradient = float3(
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw + float3(eps, 0, 0), 0) -
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw - float3(eps, 0, 0), 0),
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw + float3(0, eps, 0), 0) -
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw - float3(0, eps, 0), 0),
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw + float3(0, 0, eps), 0) -
                    _FogVolume.SampleLevel(sampler_FogVolume, uvw - float3(0, 0, eps), 0)
                );

                float3 normal = normalize(gradient + 0.001);

                // Simple lighting from top-right
                float3 lightDir = normalize(float3(0.5, 1, 0.3));
                float lighting = dot(normal, lightDir) * 0.3 + 0.7;

                // Interpolate between hidden and visible colors
                float4 color = lerp(_HiddenColor, _VisibleColor, saturate(visibility));
                color.rgb *= lighting;

                // Edge enhancement based on gradient magnitude
                float edgeStrength = length(gradient) * 5.0;
                color.a = lerp(color.a, color.a * 1.5, saturate(edgeStrength));

                return color;
            }
            ENDHLSL
        }
    }
}
