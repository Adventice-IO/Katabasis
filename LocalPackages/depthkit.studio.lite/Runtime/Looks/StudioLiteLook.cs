/************************************************************************************

Depthkit Unity SDK License v1
Copyright 2016-2024 Simile Inc dba Scatter. All Rights reserved.  

Licensed under the the Simile Inc dba Scatter ("Scatter")
Software Development Kit License Agreement (the "License"); 
you may not use this SDK except in compliance with the License, 
which is provided at the time of installation or download, 
or which otherwise accompanies this software in either electronic or hard copy form.  

You may obtain a copy of the License at http://www.depthkit.tv/license-agreement-v1

Unless required by applicable law or agreed to in writing, 
the SDK distributed under the License is distributed on an "AS IS" BASIS, 
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
See the License for the specific language governing permissions and limitations under the License. 

************************************************************************************/

using UnityEngine;

namespace Depthkit
{
    [ExecuteInEditMode]
    [AddComponentMenu("Depthkit/Studio/Built-in RP/Depthkit Studio Lite Built-in Look")]
    public class StudioLiteLook : ProceduralLook
    {
        protected static Shader s_defaultMainStencilShader = null;
        protected static Material s_defaultMainStencilMaterial = null;

        protected static Shader s_defaultMainShader = null;
        protected static Material s_defaultMainMaterial = null;

        protected static Shader s_defaultMainEdgeStencilShader = null;
        protected static Material s_defaultMainEdgeStencilMaterial = null;

        protected static Shader s_defaultMainEdgeShader = null;
        protected static Material s_defaultMainEdgeMaterial = null;

        protected static Shader s_defaultFillShader = null;
        protected static Material s_defaultFillMaterial = null;

        protected static Shader s_defaultFillEdgeShader = null;
        protected static Material s_defaultFillEdgeMaterial = null;

        public Material mainPerspectiveStencil = null;
        public Material mainPerspectiveEdgeStencil = null;
        public Material mainPerspective = null;
        public Material mainPerspectiveEdge = null;
        public Material fillPerspectives = null;
        public Material fillPerspectivesEdges = null;

        public bool showMain = true;
        public bool showMainEdge = true;
        public bool showFill = true;
        public bool showFillEdge = true;

        protected static Material GetDefaultMaterial()
        {
            if (s_defaultMainStencilShader == null)
            {
                s_defaultMainStencilShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Main Stencil Built-in RP");
            }

            if (s_defaultMainStencilMaterial == null)
            {
                s_defaultMainStencilMaterial = new Material(s_defaultMainStencilShader);
            }

            if (s_defaultMainShader == null)
            {
                s_defaultMainShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Main Photo Look Built-in RP");
            }

            if (s_defaultMainMaterial == null)
            {
                s_defaultMainMaterial = new Material(s_defaultMainShader);
            }

            if (s_defaultMainEdgeStencilShader == null)
            {
                s_defaultMainEdgeStencilShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Main Edge Stencil Built-in RP");
            }

            if (s_defaultMainEdgeStencilMaterial == null)
            {
                s_defaultMainEdgeStencilMaterial = new Material(s_defaultMainEdgeStencilShader);
            }

            if (s_defaultMainEdgeShader == null)
            {
                s_defaultMainEdgeShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Main Edge Photo Look Built-in RP");
            }

            if (s_defaultMainEdgeMaterial == null)
            {
                s_defaultMainEdgeMaterial = new Material(s_defaultMainEdgeShader);
            }

            if (s_defaultFillShader == null)
            {
                s_defaultFillShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Fill Photo Look Built-in RP");
            }

            if (s_defaultFillMaterial == null)
            {
                s_defaultFillMaterial = new Material(s_defaultFillShader);
            }

            if (s_defaultFillEdgeShader == null)
            {
                s_defaultFillEdgeShader = Shader.Find("Depthkit/Studio/Depthkit Studio Lite Fill Edge Photo Look Built-in RP");
            }

            if (s_defaultFillEdgeMaterial == null)
            {
                s_defaultFillEdgeMaterial = new Material(s_defaultFillEdgeShader);
            }

            return s_defaultMainMaterial;
        }

        public override string GetLookName() { return "Depthkit Studio Lite Photo Look"; }

        protected override void SetDefaults()
        {
            var defaultMat = GetDefaultMaterial();

            if (mainPerspective == null)
            {
                
                mainPerspective = defaultMat;
            }

            if (mainPerspectiveEdge == null)
            {
                mainPerspectiveEdge = s_defaultMainEdgeMaterial;
            }

            if (fillPerspectives == null)
            {
                fillPerspectives = s_defaultFillMaterial;
            }

            if (fillPerspectivesEdges == null)
            {
                fillPerspectivesEdges = s_defaultFillEdgeMaterial;
            }

            if(mainPerspectiveEdgeStencil == null)
            {
                mainPerspectiveEdgeStencil = s_defaultMainEdgeStencilMaterial;
            }

            if (mainPerspectiveStencil == null)
            {
                mainPerspectiveStencil = s_defaultMainStencilMaterial;
            }

            if (lookMaterial == null)
            {
                lookMaterial = mainPerspective;
            }

            base.SetDefaults();
        }

        protected override void SetDataSources()
        {
            if (meshSource == null)
            {
                meshSource = depthkitClip.GetDataSource<Depthkit.StudioLiteMeshSource>();
            }
        }
        protected override void OnUpdate()
        {
            StudioLiteMeshSource shell = meshSource as StudioLiteMeshSource;

            if(showMain)
            {
                lookMaterial = mainPerspectiveStencil;
                shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.Main;
                base.OnUpdate();                
            }

            if(showMainEdge)
            {
                lookMaterial = mainPerspectiveEdgeStencil;
                shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.MainEdges;
                base.OnUpdate();
            }

            if (shell.maxPerspectivesToRender > 1)
            {
                if(showFillEdge)
                {
                    lookMaterial = fillPerspectivesEdges;
                    shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.FillEdges;
                    base.OnUpdate();
                }

                if(showFill)
                {
                    lookMaterial = fillPerspectives;
                    shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.Fill;
                    base.OnUpdate();
                }
            }

            if (showMain)
            {
                lookMaterial = mainPerspective;
                shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.Main;
                base.OnUpdate();
            }

            if (showMainEdge)
            {
                lookMaterial = mainPerspectiveEdge;
                shell.currentSubmeshIndex = (int)StudioLiteMeshSource.SubMeshIndex.MainEdges;
                base.OnUpdate();
            }
        }
    }
}