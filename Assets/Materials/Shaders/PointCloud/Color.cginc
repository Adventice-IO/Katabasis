
float4 get_color(float4 color, float4 purple_key) {
    
    // gamma conversion
    // color.rgb = GammaToLinearSpace(color.rgb);
    float4 color_t = color;
    // float4 color_t = pow(color, float4(2.2, 2.2, 2.2, 1));
// #if UNITY_PLATFORM_ANDROID
//     color.rgb = LinearToGammaSpace(color.rgb);
// #endif
    color.rgb = color.rgb * color.rgb * 1.5;

    //float dcolor = length(color);
    //float black = saturate(0.05/max(0.0001, dcolor*dcolor));
    //color = lerp(color, 0.4, black);
    float purple = saturate(0.01/max(0.0001,length(rgb2hsv(purple_key) - rgb2hsv(saturate(color_t)))));
    //color = lerp(color, 4.0, smoothstep(0.0, 0.1, purple));
    color = lerp(color, clamp(Luminance(color_t), 0.5, 1.0), smoothstep(0.0, 0.1, purple));
    // color = lerp(color, float4(1,0,0,1), smoothstep(0.0, 0.1, purple));
    
    // increment saturation
    // float3 hsv = rgb2hsv(color.rgb);
    // hsv.y *= 1.5;
    // color.rgb = hsv2rgb(hsv);

    return color;
}

float4 get_color_archives(float4 color, float4 purple_key) {
    
    // gamma conversion
    //color.rgb = color.rgb * color.rgb;// * 1.5;
    //color.rgb *= 2.0;

    //float dcolor = length(color);
    //float black = saturate(0.05/max(0.0001, dcolor*dcolor));
    //color = lerp(color, 0.4, black);
    
    float purple = saturate(0.005/max(0.0001,length(rgb2hsv(purple_key) - rgb2hsv(color))));
    
    // increment saturation
    //float3 hsv = rgb2hsv(color.rgb);
    //hsv.y *= 1.5;
    //color.rgb = hsv2rgb(hsv);

    //color = lerp(color, 4.0, smoothstep(0.0, 0.1, purple));
    //color = lerp(color, clamp(Luminance(color)*2., 0.5, 1.0), smoothstep(0.0, 0.1, purple));

    return color;
}