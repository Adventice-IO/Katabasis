using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BAPointCloudRenderer.Controllers
{
    /// <summary>
    /// Used internally for Previewing. Please don't attach.
    /// </summary>
    
    [ExecuteInEditMode]
    public class PreviewObject : MonoBehaviour
    {
        MaterialPropertyBlock block;
        GraphicsBuffer masksBuffer;
        int masksCount = 0;

        public void Start()
        {

        }

        public void Update()
        {
            if (block == null) block = new MaterialPropertyBlock();

            Renderer render = GetComponent<Renderer>();
            if (render == null) return;

            render.GetPropertyBlock(block);
            block.SetBuffer("_MaskBoxes", masksBuffer);
            block.SetInt("_MaskCount", masksCount);
            render.SetPropertyBlock(block);
        }
        public void OnDestroy()
        {
            //Debug.Log("Preview Object Destroyed");
        }

        public void updateMasks(GraphicsBuffer maskBuffer, int count)
        {
            masksBuffer = maskBuffer;
            masksCount = count;

        }
    }
}

