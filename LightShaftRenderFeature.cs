namespace UnityEngine.Rendering.Universal
{
    public class LightShaftRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PassSetting
        {
            public string m_profilerTag = "LightShaft RenderFeature";
            public RenderPassEvent m_passEvent = RenderPassEvent.AfterRenderingTransparents;
            public Shader m_shader;
            public ComputeShader m_computeShader;
        }
    
        public PassSetting m_setting = new PassSetting();
        LightShaftRenderPass m_LightShaftRenderPass;
    
        public override void Create()
        {
            m_LightShaftRenderPass = new LightShaftRenderPass(m_setting);
        }
    
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var lightShaftVolume = VolumeManager.instance.stack.GetComponent<LightShaft_Volume>();

            if (lightShaftVolume != null && lightShaftVolume.IsActive())
            {
                m_LightShaftRenderPass.Setup(lightShaftVolume);
                renderer.EnqueuePass(m_LightShaftRenderPass);
            }
        }
    }   
}


