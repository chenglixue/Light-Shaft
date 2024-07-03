using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    class LightShaftRenderPass : ScriptableRenderPass
    {
        #region Variable
        private LightShaftRenderFeature.PassSetting m_passSetting;
        private Material m_material;
        private ComputeShader m_computeShader;
        private LightShaft_Volume m_lightShaftVolume;
        
        private RenderTextureDescriptor m_descriptor;
        private RenderTargetIdentifier m_cameraColorIden;
        private int m_tempRTID = Shader.PropertyToID("_TempRT");
        private int m_sourceRTID = Shader.PropertyToID("_SourceRT");
        private Vector2Int m_texSize;
        #endregion

        #region Setup
        public LightShaftRenderPass(LightShaftRenderFeature.PassSetting passSetting)
        {
            this.m_passSetting = passSetting;
            renderPassEvent = m_passSetting.m_passEvent;
            
            if (m_passSetting.m_shader == null)
            {
                Debug.LogError("Custom: Shader not found.");
                m_material = CoreUtils.CreateEngineMaterial("LightShaft");
            }
            else
            {
                m_material = new Material(m_passSetting.m_shader);
            }

            m_computeShader = m_passSetting.m_computeShader;
        }

        public void Setup(LightShaft_Volume lightShaftVolume)
        {
            m_lightShaftVolume = lightShaftVolume;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_descriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_descriptor.msaaSamples = 1;
            m_descriptor.enableRandomWrite = true;
            m_descriptor.depthBufferBits = 0;
            cmd.GetTemporaryRT(m_sourceRTID, m_descriptor, FilterMode.Bilinear);
            m_descriptor.width /= 2;
            m_descriptor.height /= 2;
            m_texSize = new Vector2Int(m_descriptor.width, m_descriptor.height);
            m_cameraColorIden = renderingData.cameraData.renderer.cameraColorTarget;
            
            cmd.GetTemporaryRT(m_tempRTID, m_descriptor, FilterMode.Bilinear);
            
            if (m_material != null)
            {
                m_material.SetInt("_MaxDepth", m_lightShaftVolume.m_maxDepth.value);
                m_material.SetFloat("_MaxDistance", m_lightShaftVolume.m_maxDistance.value);
                m_material.SetFloat("_Brightness", m_lightShaftVolume.m_brightness.value);
                m_material.SetFloat("_ScatterFactor", m_lightShaftVolume.m_scatterFactor.value);
                m_material.SetFloat("_HeightFromSeaLevel", m_lightShaftVolume.m_heightFromSeaLevel.value);
                m_material.SetColor("_LightShaftColor", m_lightShaftVolume.m_lightShaftColor.value);
                m_material.SetVector("_TexParams", GetTextureSizeParams(m_texSize));
                m_material.SetTexture("_BlueNoiseTex", m_lightShaftVolume.m_blueNoiseTex.value);
            }
        }
        #endregion

        #region Execute

        Vector4 GetTextureSizeParams(Vector2Int texSize)
        {
            return new Vector4(texSize.x, texSize.y, 1f / texSize.x, 1f / texSize.y);
        }
        
        private void DoKawaseSample(CommandBuffer cmd, RenderTargetIdentifier sourceid, RenderTargetIdentifier targetid,
                                Vector2Int sourceSize, Vector2Int targetSize,
                                float offset, bool downSample, ComputeShader computeShader)
        {
            if (!computeShader) return;
            string kernelName = downSample ? "DualBlurDownSample" : "DualBlurUpSample";
            int kernelID = computeShader.FindKernel(kernelName);
            computeShader.GetKernelThreadGroupSizes(kernelID, out uint x, out uint y, out uint z);
            cmd.SetComputeTextureParam(computeShader, kernelID, "_SourceTex", sourceid);
            cmd.SetComputeTextureParam(computeShader, kernelID, "_RW_TargetTex", targetid);
            cmd.SetComputeVectorParam(computeShader, "_SourceSize", GetTextureSizeParams(sourceSize));
            cmd.SetComputeVectorParam(computeShader, "_TargetSize", GetTextureSizeParams(targetSize));
            cmd.SetComputeFloatParam(computeShader, "_BlurOffset", offset);
            cmd.DispatchCompute(computeShader, kernelID,
                                Mathf.CeilToInt((float)targetSize.x / x),
                                Mathf.CeilToInt((float)targetSize.y / y),
                                1);
        }

        private void DoKawaseLinear(CommandBuffer cmd, RenderTargetIdentifier sourceid, RenderTargetIdentifier targetid,
            Vector2Int sourceSize, float offset, ComputeShader computeShader)
        {
            if (!computeShader) return;
            string kernelName = "LerpDownUpTex";
            int kernelID = computeShader.FindKernel(kernelName);
            computeShader.GetKernelThreadGroupSizes(kernelID, out uint x, out uint y, out uint z);
            cmd.SetComputeTextureParam(computeShader, kernelID, "_SourceTex", sourceid);
            cmd.SetComputeTextureParam(computeShader, kernelID, "_RW_TargetTex", targetid);
            cmd.SetComputeVectorParam(computeShader, "_SourceSize", GetTextureSizeParams(sourceSize));
            cmd.SetComputeFloatParam(computeShader, "_BlurOffset", offset);
            cmd.DispatchCompute(computeShader, kernelID,
                                Mathf.CeilToInt((float)sourceSize.x / x),
                                Mathf.CeilToInt((float)sourceSize.y / y),
                                1);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler(m_passSetting.m_profilerTag)))
            {
                cmd.Blit(m_cameraColorIden, m_sourceRTID);
                cmd.Blit(m_cameraColorIden, m_tempRTID, m_material, 0);

                List<int> RTIDs = new List<int>();
                List<Vector2Int> RTSizes = new List<Vector2Int>();
                var tempDesc = m_descriptor;
                
                int kawaseRTID = Shader.PropertyToID("_KawaseRT");
                cmd.GetTemporaryRT(kawaseRTID, tempDesc);
                RTIDs.Add(kawaseRTID);
                RTSizes.Add(m_texSize);
                
                float downSampleAmount = Mathf.Log(m_lightShaftVolume.GetRadius() + 1.0f) / 0.693147181f;
                int downSampleCount = Mathf.FloorToInt(downSampleAmount);
                float offsetRatio = downSampleAmount - (float)downSampleCount;
                
                var lastRTSize = m_texSize;
                int lastRTID = m_tempRTID;
                for (int i = 0; i <= downSampleCount; ++i)
                {
                    int currRTID = Shader.PropertyToID("_KawaseRT" + i.ToString());
                    var currRTSize = new Vector2Int((lastRTSize.x + 1) / 2, (lastRTSize.y + 1) / 2);
                    tempDesc.width = currRTSize.x;
                    tempDesc.height = currRTSize.y;
                    cmd.GetTemporaryRT(currRTID, tempDesc);
                
                    RTIDs.Add(currRTID);
                    RTSizes.Add(currRTSize);
                
                    DoKawaseSample(cmd, lastRTID, currRTID, lastRTSize, currRTSize,
                        1f, true, m_computeShader);
                
                    lastRTID = currRTID;
                    lastRTSize = currRTSize;
                }
                if(downSampleCount == 0)
                {
                    DoKawaseSample(cmd, RTIDs[1], RTIDs[0], RTSizes[1], RTSizes[0], 1.0f, false, m_computeShader);
                    DoKawaseLinear(cmd, m_tempRTID, RTIDs[0], RTSizes[0], offsetRatio, m_computeShader);
                }
                else
                {
                    string intermediateRTName = "_KawaseRT" + (downSampleCount + 1).ToString();
                    int intermediateRTID = Shader.PropertyToID(intermediateRTName);
                    Vector2Int intermediateRTSize = RTSizes[downSampleCount];
                    tempDesc.width = intermediateRTSize.x;
                    tempDesc.height = intermediateRTSize.y;
                    cmd.GetTemporaryRT(intermediateRTID, tempDesc);
                    
                    for (int i = downSampleCount+1; i >= 1; i--)
                    {
                        int sourceID = RTIDs[i];
                        Vector2Int sourceSize = RTSizes[i];
                        int targetID = i == (downSampleCount + 1) ? intermediateRTID : RTIDs[i - 1];
                        Vector2Int targetSize = RTSizes[i - 1];
                    
                        DoKawaseSample(cmd, sourceID, targetID, sourceSize, targetSize, 1.0f, false, m_computeShader);
                    
                        if (i == (downSampleCount + 1))
                        {
                            DoKawaseLinear(cmd, RTIDs[i - 1], intermediateRTID, targetSize, offsetRatio, m_computeShader);
                            int tempID = intermediateRTID;
                            intermediateRTID = RTIDs[i - 1];
                            RTIDs[i - 1] = tempID;
                        }
                        cmd.ReleaseTemporaryRT(sourceID);
                    }
                    cmd.ReleaseTemporaryRT(intermediateRTID);
                }

                cmd.Blit(RTIDs[0], m_tempRTID);
                cmd.Blit(RTIDs[0], m_cameraColorIden, m_material, 1);
                cmd.ReleaseTemporaryRT(kawaseRTID);
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(m_tempRTID);
            cmd.ReleaseTemporaryRT(m_sourceRTID);
        }
        #endregion
    }   
}
