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

#ifndef _DEPTHKIT_DEFINES_CGINC
#define _DEPTHKIT_DEFINES_CGINC

#define DK_BRIGHTNESS_THRESHOLD_OFFSET 0.01f
#define DK_MAX_NUM_PERSPECTIVES 10

#define DK_CORRECT_NONE 0
#define DK_CORRECT_LINEAR_TO_GAMMA 1
#define DK_CORRECT_GAMMA_TO_LINEAR 2
//Unity 2017.1 - 2018.2 has a video player bug where Linear->Gamma needs to be applied twice before texture look up in depth
#define DK_CORRECT_LINEAR_TO_GAMMA_2X 3

#endif