#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
half4 _MainTex_TexelSize;

CBUFFER_END

half _HeightFromSeaLevel;   // 距离海平面高度
half _ScatterFactor;        // 散射系数
uint _MaxDepth;             // 最大递进深度(次数)
half _MaxDistance;
half _Brightness;
half4 _LightShaftColor;
half4 _TexParams;

TEXTURE2D(_MainTex);                            SAMPLER(sampler_MainTex);
TEXTURE2D(_SourceRT);                           SAMPLER(sampler_SourceRT);
TEXTURE2D(_BlueNoiseTex);                       SAMPLER(sampler_BlueNoiseTex);
TEXTURE2D_X_FLOAT(_CameraDepthTexture);         SAMPLER(sampler_CameraDepthTexture);  
TEXTURE2D_X_FLOAT(_TempRT);                     SAMPLER(sampler_TempRT);
SamplerState sampler_LinearClamp;
SamplerState sampler_PointClamp;

struct VSInput
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSInput
{
    float2 uv : TEXCOORD0;
    
    float4 positionCS : SV_POSITION;
    float4 positionSS : TEXCOORD2;
    float3 positionWS : TEXCOORD3;

    float4 uv01 : TEXCOORD4;
    float4 uv23 : TEXCOORD5;
    float4 uv45 : TEXCOORD6;
    float4 uv67 : TEXCOORD7;
};

float Pow2( float x )
{
    return x * x;
}

float2 Pow2( float2 x )
{
    return x*x;
}

float3 Pow2( float3 x )
{
    return x*x;
}

float4 Pow2( float4 x )
{
    return x*x;
}

PSInput LightShaftVS(VSInput i)
{
    PSInput o;

    VertexPositionInputs positionData = GetVertexPositionInputs(i.positionOS);
    o.positionCS = positionData.positionCS;
    o.positionSS = ComputeScreenPos(o.positionCS);
    o.positionWS = positionData.positionWS;

    // dx反转uv
    o.uv = i.uv;  // depth uv
    #if defined (UNITY_UV_STARTS_AT_TOP)
    if (_MainTex_TexelSize.y < 0.h)
    {
        o.uv.y = 1 - o.uv.y;
    }
    #endif

    return o;
}

float3 ReConstructPosWS(float2 posVP)
{
    half depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, posVP);

    float3 posNDC = float3(posVP * 2.f - 1.f, depth);
    #if defined(UNITY_UV_STARTS_AT_TOP)
    posNDC.y = 1.f - posNDC.y;
    #endif

    float4 posWS = mul(UNITY_MATRIX_I_VP, float4(posNDC, 1.f));
    posWS.xyz /= posWS.w;

    return posWS;
}
float GetShadow(float3 positionWS)
{
    float4 shadowUV = TransformWorldToShadowCoord(positionWS);
    float shadow = MainLightRealtimeShadow(shadowUV);

    return shadow;
}
// 沿视线方向散射的量(密度函数)
float GetP(float cosTheta)
{
    return 0.0596831f * (1.f + Pow2(cosTheta));
}
float GetRho()
{
    return exp(-_HeightFromSeaLevel / 8400.f);
}
float GetScatter(float cosTheta)
{
    return GetP(cosTheta) * _ScatterFactor;
}
float GetTransmittance(float distance)
{
    return exp(-distance * _ScatterFactor * GetRho());
}
half3 GetLightShaft(float3 viewOrigin, half3 viewDir, float maxDistance, float2 uv)
{
    Light mainLight = GetMainLight();
    half3 mainLightDir = mainLight.direction;
    
    half rayMarchingStep = maxDistance / _MaxDepth;              // 步长
    half currDistance = 0.h;         // 当前已经步进的距离
    float3 currPos = viewOrigin;
    half3 totalLight = 0.h;

    float scatterFun = GetScatter(dot(viewDir, -mainLightDir));

    UNITY_UNROLL(50);
    for(int i = 0; i < _MaxDepth; ++i)
    {
        rayMarchingStep *= 1.02f;

        currDistance += rayMarchingStep;
        if(currDistance > maxDistance) break;

        // 步进后新的位置
        currPos += viewDir * rayMarchingStep * _BlueNoiseTex.SampleLevel(sampler_LinearClamp, uv, 0).r;
        float shadow = GetShadow(currPos);
        // 求当前pixel的阴影值
        totalLight += _Brightness * shadow * scatterFun * GetTransmittance(rayMarchingStep);
    }
    
    half3 result = totalLight * mainLight.color * _LightShaftColor.rgb * _LightShaftColor.aaa;
    
    return result;
}

half4 LightShaftPS(PSInput i) : SV_TARGET
{
    float2 channel = floor(i.positionCS);
    // 棋盘格刷新
    clip(channel.y%2 * channel.x%2 + (channel.y+1)%2 * (channel.x+1)%2 - 0.1f);
    
    float2 posVP = (i.positionCS - 0.5f) * _TexParams.zw;
    float3 rePosWS = ReConstructPosWS(posVP);

    // ray
    half3 viewDir = SafeNormalize(rePosWS - _WorldSpaceCameraPos);   
    float3 viewOrigin = _WorldSpaceCameraPos;
    float totalDistance = min(length(rePosWS - _WorldSpaceCameraPos), _MaxDistance);    // 总距离
    
    half3 lightShaft = GetLightShaft(viewOrigin, viewDir, totalDistance, posVP);
    
    return half4(lightShaft, 1);
}

PSInput AddVS(VSInput i)
{
    PSInput o;

    o.positionCS = TransformObjectToHClip(i.positionOS);

    // dx反转uv
    o.uv = i.uv;
    #if defined (UNITY_UV_STARTS_AT_TOP)
    if (_MainTex_TexelSize.y < 0.h)
    {
        o.uv.y = 1 - o.uv.y;
    }
    #endif

    return o;
}

half4 AddPS(PSInput i) : SV_TARGET
{
    half4 result = 0.h;
    
    half4 sourceTex = SAMPLE_TEXTURE2D(_SourceRT, sampler_SourceRT, i.uv);
    half4 blurTex = SAMPLE_TEXTURE2D(_TempRT, sampler_TempRT, i.uv);
    result += sourceTex + blurTex;

    return half4(result.xyz, 1.h);
}