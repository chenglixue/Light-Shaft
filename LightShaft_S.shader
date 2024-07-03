Shader "LightShaft"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "LightShaft.hlsl"
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS                    //接受阴影
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE            //产生阴影
        #pragma multi_compile _ _SHADOWS_SOFT                         //软阴影 
        ENDHLSL
        
        Pass
        {
            Name "Light Shaft Pass"
            
            HLSLPROGRAM
            #pragma vertex LightShaftVS
            #pragma fragment LightShaftPS
            ENDHLSL
        }

        Pass
        {
            NAME "Add LightShaft with blur"
            HLSLPROGRAM
            #pragma vertex AddVS
            #pragma fragment AddPS
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
