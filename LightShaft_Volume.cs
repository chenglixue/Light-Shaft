using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Elysia/Elysia Light Shaft", typeof(UniversalRenderPipeline))]
    public class LightShaft_Volume : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("是否启用 体积光")]
        public BoolParameter m_Enable = new BoolParameter(false);
        public bool IsActive() => m_Enable.value; 
        public bool IsTileCompatible() => false;
        
        [Tooltip("步进深度")] 
        [Range(1, 16)] 
        public ClampedIntParameter m_maxDepth = new ClampedIntParameter(16, 1, 16);
        
        [Tooltip("步进最大距离")]
        public FloatParameter m_maxDistance = new FloatParameter(400f);
        
        [Tooltip("散射系数")] 
        public ClampedFloatParameter m_scatterFactor = new ClampedFloatParameter(1f, 0f, 2f);
        
        [Tooltip("距离海平面高度")]
        public ClampedFloatParameter m_heightFromSeaLevel = new ClampedFloatParameter(0f, 0f, 8400f);

        public TextureParameter m_blueNoiseTex = new TextureParameter(null);
        
        [Tooltip("体积光颜色")] 
        public ColorParameter m_lightShaftColor = new ColorParameter(Color.white, true, true, false);
        
        [Tooltip("LightShaft亮度")] 
        public ClampedFloatParameter m_brightness = new ClampedFloatParameter(1f, 0f, 1f);
        
        public ClampedFloatParameter blurIntensity = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter blurMaxRadius = new ClampedFloatParameter(32f, 0f, 255f);
        
        public float GetRadius()
        {
            return blurIntensity.value * blurMaxRadius.value;
        }
    }   
}