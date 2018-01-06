Shader "Voxel/VoxelResolutionShader" {
	Properties {
		_PixelScale ("Pixel Scale", Vector) = (1,1,1,0.1)
		_Color0 ("Color 0", Color) = (1,1,1,0.25)
		_Color1 ("Color 1", Color) = (0,0,0,0.25)
	}
	Subshader {
		Tags { "Queue"="Transparent" }
		Pass {
			ZWrite Off
			ColorMask RGB
			//Blend DstColor Zero
			Blend SrcAlpha OneMinusSrcAlpha
			//Offset -1, -1

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#include "UnityCG.cginc"
			
			struct v2f {
				float4 uvShadow : TEXCOORD0;
				float4 uvFalloff : TEXCOORD1;
				float4 pos : SV_POSITION;
			};
			
			float4x4 unity_Projector;
			float4x4 unity_ProjectorClip;

			float4 _PixelScale;

			fixed4 _Color0;
			fixed4 _Color1;

			v2f vert(float4 vertex : POSITION) {
				v2f o;
				o.pos = UnityObjectToClipPos(vertex);
				o.uvShadow = mul (unity_Projector, vertex);
				o.uvFalloff = mul (unity_ProjectorClip, vertex);
				return o;
			}
			
			fixed4 frag(v2f i) : SV_Target {
				float3 pos = float3(i.uvShadow.xy - 0.5, i.uvShadow.z*0.5);
				fixed3 bounds_test = abs(pos) < 0.5;
				//float is_inside = 1 - dot(abs(pos) > 0.5, 1);
				float is_inside = bounds_test.x*bounds_test.y*bounds_test.z;

				float3 pix2 = frac((pos+0.5) * _PixelScale.xyz * 0.5) < 0.5;
				fixed bright = frac(dot(pix2, 0.5)) < 0.5;
				return lerp(_Color0, _Color1, bright)*fixed4(1,1,1,is_inside);

//				float3 pix = abs(frac((pos+0.5) * _PixelScale.xyz) - 0.5)*2;
//				float3 wrk = float3(min(pix.x, pix.y), min(pix.x, pix.z), min(pix.y, pix.z));
//				float2 dist = (1 - _PixelScale.w) + float2(-0.05, 0.05);
//				fixed bright1 = smoothstep(dist.x, dist.y, max(max(pix.x, pix.y), pix.z));
//				fixed bright2 = smoothstep(dist.x, dist.y, max(max(wrk.x, wrk.y), wrk.z));
//				return lerp(_Color0, _Color1, lerp(bright1, bright2, 0.5))*fixed4(1,1,1,is_inside);
			}
			ENDCG
		}
	}
}
