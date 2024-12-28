Shader "Unlit/ParticleShader"
{
    Properties
    {
        _Colormap("Color map", 2D) = "" {}
        _Smoothmap("SmoothAO map", 2D) = "" {}
        _Normalmap("Normal map", 2D) = "" {}
        _Noisemap("Noise map", 2D) = "" {}  
        _Color0("Example color", Color) = (.0, .0, .0, 1.0)
        _Color1("Example color", Color) = (1.0, 1.0, 1.0, 1.0)
        _SubsurfaceColor("Subsurface Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Thinness("Thinness", Float) = 1.0
        _Scatter("Scatter", Float) = 1.0
        _Burn("Burn", Float) = 1.0
    }

    SubShader
    {
        //Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        Tags {"Queue"="Geometry" "IgnoreProjector"="True" "RenderType"="Opaque"}
        //BlendOp Add
        //Blend SrcAlpha OneMinusSrcAlpha
        LOD 100
        Cull Off

        Pass
        {        
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma shader_feature_local _NOISEMAP
            #pragma shader_feature_local _VFX

            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma shader_feature _ _SAMPLE_GI
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT
            #define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
            #define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
            #define VARYINGS_NEED_POSITION_WS
            #define VARYINGS_NEED_NORMAL_WS
            #define FEATURES_GRAPH_VERTEX
            /* WARNING: $splice Could not find named fragment 'PassInstancing' */
            #define SHADERPASS SHADERPASS_UNLIT
            #define _FOG_FRAGMENT 1

            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

            struct vertdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint vid : SV_VertexID;
                uint iid : SV_InstanceID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                uint iid : TEXCOORD1;
                float height : TEXCOORD2;
                float3 normal : TEXCOORD3;
                float3 normal2 : TEXCOORD4;
                float3 worldPos : TEXCOORD5;
                float3 worldUV : TEXCOORD6;
                float2 flow : TEXCOORD7;
            };

            struct fragdata
            {
                float4 col : SV_Target;
            };

            TEXTURE2D(_Colormap);
            SAMPLER(sampler_Colormap);
            TEXTURE2D(_Smoothmap);
            SAMPLER(sampler_Smoothmap);
            TEXTURE2D(_Normalmap);
            SAMPLER(sampler_Normalmap);
            TEXTURE2D(_Noisemap);
            SAMPLER(sampler_Noisemap);

            sampler2D normalmap;
            sampler2D noisemap;

            CBUFFER_START(UnityPerMaterial)
                float4 _Colormap_ST;
                float4 _Smoothmap_ST;
                float4 _Normalmap_ST;
                half4 _Color0;
                half4 _Color1;
                half4 _SubsurfaceColor;
                float _Thinness;
                float _Scatter;
                float _Burn;
            CBUFFER_END

            StructuredBuffer<float3> vertexBuffer;
            StructuredBuffer<float3> normalBuffer;
            StructuredBuffer<float2> uvBuffer;
            StructuredBuffer<float4> culledPositionsBuffer;

            float4 seed;
            float4 noisemapIntensity;
            float modelHeight;
            float worldScale;
            float time;
            float4 repulsor;
            float4 wind;
            uint spawnersCountX;

            float3 camPos; //ws

            float random2D(float2 xy, float2 dir)
            {
                float val = dot(xy, dir);
                return frac(159.15 * sin(val));
            }

            float3 interpolateTranslation(float t, float3 cp0, float3 cp1, float3 cp2)
            {
                float3 interpolatedTranslation = (1.0f - t) * (1.0f - t) * cp0 + 2.0f * (1.0f - t) * t * cp1 + t * t * cp2;
                return interpolatedTranslation;
            }

            struct FoliageLightingData {
                // Position and orientation
                float3 positionWS;
                float3 normalWS;
                float3 viewDirectionWS;
                float4 shadowCoord;

                // Surface attributes
                float3 albedo;
                float smoothness;
                float ambientOcclusion;

                // Baked lighting
                float3 bakedGI;
                float4 shadowMask;
                float fogFactor;
            };

            // Translate a [0, 1] smoothness value to an exponent 
            float GetSmoothnessPower(float rawSmoothness) {
                return exp2(10 * rawSmoothness + 1);
            }

            float3 FoliageGlobalIllumination(FoliageLightingData d) {
                float3 indirectDiffuse = d.albedo * d.bakedGI * d.ambientOcclusion;

                float3 reflectVector = reflect(-d.viewDirectionWS, d.normalWS);
                // This is a rim light term, making reflections stronger along
                // the edges of view
                float fresnel = Pow4(1 - saturate(dot(d.viewDirectionWS, d.normalWS)));
                // This function samples the baked reflections cubemap
                // It is located in URP/ShaderLibrary/Lighting.hlsl
                float3 indirectSpecular = GlossyEnvironmentReflection(reflectVector,
                    RoughnessToPerceptualRoughness(1 - d.smoothness),
                    d.ambientOcclusion) * fresnel;

                return indirectDiffuse + indirectSpecular;
            }

            float3 FoliageLightHandling(FoliageLightingData d, Light light) {

                float3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);

                float diffuse = saturate(dot(d.normalWS, light.direction));
                float specularDot = saturate(dot(d.normalWS, normalize(light.direction + d.viewDirectionWS)));
                float specular = pow(specularDot, GetSmoothnessPower(d.smoothness)) * diffuse;

                float3 color = d.albedo * radiance * (diffuse + specular);

                return color;
            }

            float3 CalculateFoliageLighting(FoliageLightingData d) {

                // Get the main light. Located in URP/ShaderLibrary/Lighting.hlsl
                Light mainLight = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);

                // In mixed subtractive baked lights, the main light must be subtracted
                // from the bakedGI value. This function in URP/ShaderLibrary/Lighting.hlsl takes care of that.
                MixRealtimeAndBakedGI(mainLight, d.normalWS, d.bakedGI);
                float3 color = FoliageGlobalIllumination(d);
                // Shade the main light

                color = FoliageLightHandling(d, mainLight);

                #ifdef _ADDITIONAL_LIGHTS
                    // Shade additional cone and point lights. Functions in URP/ShaderLibrary/Lighting.hlsl
                    uint numAdditionalLights = GetAdditionalLightsCount();
                    for (uint lightI = 0; lightI < numAdditionalLights; lightI++) {
                        Light light = GetAdditionalLight(lightI, d.positionWS, d.shadowMask);
                        color += FoliageLightHandling(d, light);
                    }
                #endif

                color = MixFog(color, d.fogFactor);
                return color;
            }   

            void CalculateFoliageLighting_float(float3 Position, float3 Normal, float3 ViewDirection,
                float3 Albedo, float Smoothness, float AmbientOcclusion,
                float2 LightmapUV,
                out float3 Color) {

                FoliageLightingData d;
                d.positionWS = Position;
                d.normalWS = Normal;
                d.viewDirectionWS = ViewDirection;
                d.albedo = Albedo;
                d.smoothness = Smoothness;
                d.ambientOcclusion = AmbientOcclusion;

                // Calculate the main light shadow coord
                // There are two types depending on if cascades are enabled
                float4 positionCS = TransformWorldToHClip(Position);

                #if SHADOWS_SCREEN
                    d.shadowCoord = ComputeScreenPos(positionCS);
                #else
                    d.shadowCoord = TransformWorldToShadowCoord(Position);
                #endif

                // The following URP functions and macros are all located in
                // URP/ShaderLibrary/Lighting.hlsl
                // Technically, OUTPUT_LIGHTMAP_UV, OUTPUT_SH and ComputeFogFactor
                // should be called in the vertex function of the shader. However, as of
                // 2021.1, we do not have access to custom interpolators in the shader graph.

                // // The lightmap UV is usually in TEXCOORD1
                // // If lightmaps are disabled, OUTPUT_LIGHTMAP_UV does nothing
                float2 lightmapUV;
                OUTPUT_LIGHTMAP_UV(LightmapUV, unity_LightmapST, lightmapUV);

                // Samples spherical harmonics, which encode light probe data
                float3 vertexSH;
                //OUTPUT_SH(Normal, vertexSH);
                // This function calculates the final baked lighting from light maps or probes
                d.bakedGI = SAMPLE_GI(lightmapUV, vertexSH, Normal);

                // This function calculates the shadow mask if baked shadows are enabled
                d.shadowMask = SAMPLE_SHADOWMASK(lightmapUV);
                // This returns 0 if fog is turned off
                // It is not the same as the fog node in the shader graph
                d.fogFactor = ComputeFogFactor(positionCS.z);
                
                Color = CalculateFoliageLighting(d);
            }

            v2f vert(vertdata v)
            {
                v2f o;
                o.uv = uvBuffer[v.vid];
                o.iid = v.iid;

                float3 instanceOffset = culledPositionsBuffer[v.iid].xyz;
                float3 vPos = vertexBuffer[v.vid];
                float3 vNorm = normalBuffer[v.vid];
                float3 worldPos = vPos + instanceOffset;

                float x = worldPos.x / (worldScale / 2.0f);
                x = x * 0.5f + 0.5f;
                float y = worldPos.z / (worldScale / 2.0f);
                y = y * 0.5f + 0.5f;

                float4 dirmap = tex2Dlod(normalmap, float4(x, y, 0, 2)); // [0,1]
                float h = 1.0 - (worldPos.y / modelHeight);
                float3 offsetDir = dirmap.xyz * float3(2.0, 2.0, 2.0) - float3(1.0, 1.0, 1.0); // [-1, 1]
                offsetDir.xz *= 5. ;
                offsetDir.y *= 10.;    

                if(_VFX){
                    worldPos = lerp(worldPos + offsetDir * (1.0 - h), worldPos, 1.0 - dirmap.y);
                } else {
                    offsetDir = float3(0.5, 10.0, 0.5);
                    worldPos = lerp(worldPos + offsetDir * (1.0 - h), worldPos, 0.0);
                }

                if(_NOISEMAP){
                    float x_1 = x + time * wind.x;
                    float4 noise = tex2Dlod(noisemap, float4(x_1 * wind.y , y * wind.y, 0, 0)); // [0 ,1]
                    float2 windOffset = float2(noise.x, noise.z);
                    windOffset -= float2(0.5, 0.5); // [-0.5, 0.5]
                    windOffset *= wind.z;
                    float3 windPos = worldPos + float3(windOffset.x, 0.0, windOffset.y);
                    worldPos = lerp(worldPos, windPos, 1.0-h);

                    float bump = length(float2((worldPos.x / ((worldScale * 3.0)/2) * 4), (worldPos.z / ((worldScale * 3.0)/2) * 4) ) );
                    bump = 1.0 - smoothstep(0.0, 0.5, bump);
                    bump -=  tex2Dlod(noisemap, float4(x *2  , y *2, 0, 0)).x;
                    worldPos.y = lerp(worldPos.y, worldPos.y + bump * 15, 1.0 - h);

                    if(_VFX){
                        worldPos.y = worldPos.y + 2.0 * ( 1.0 - smoothstep(0.0, repulsor.z, (length(float2(repulsor.x, repulsor.y) - float2(x,y)))/ 0.025) );
                    }
                }

                float4 worldOffset = float4(0,0,0,0);
                worldOffset.x = unity_ObjectToWorld[0][3];
                worldOffset.z = unity_ObjectToWorld[2][3];
                worldPos = worldPos + worldOffset;
                o.worldPos = worldPos;
                o.vertex = TransformWorldToHClip(worldPos);
                o.height = (1.0 - dirmap.w);
                
                float3 n1 = vNorm;
                float3 n2 = float3(dirmap.x, 1.0 - dirmap.y, dirmap.z) * 2.0 - 1.0;
                o.normal = lerp(n1, n2, dirmap.w);
                o.normal2 = n1;
                o.worldUV = float3(x, y, worldPos.y / modelHeight );
                o.flow = float2(dirmap.x, dirmap.z);

                return o;
            }

            fragdata frag(v2f i)
            {
                fragdata fragout;
 
                float4 colmap = SAMPLE_TEXTURE2D(_Colormap, sampler_Colormap, i.uv);
                float4 smoothmap = SAMPLE_TEXTURE2D(_Smoothmap, sampler_Smoothmap, i.uv);
                float4 normalmap = SAMPLE_TEXTURE2D(_Normalmap, sampler_Normalmap, i.uv);

                float3 norm;
                if(_VFX){
                    norm = i.normal;
                } else {
                    norm = i.normal2;
                }
                float3 norm2 = i.normal2;

                float a = colmap.w;
                float3 color = colmap.xyz;
                float3 variance =  SAMPLE_TEXTURE2D(_Noisemap, sampler_Noisemap, i.worldUV.xz * .5).rgb;
                color = lerp(color, _Color1.xyz, min(1.0, variance.y *.25 + variance.x *.25 * _Color1.w ));

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float3 radiance = mainLight.color;
                float3 translucencyRadiance = radiance * _SubsurfaceColor.xyz;

                float3 viewDir = normalize(camPos - i.worldPos);
                float3 reflectDir = reflect(lightDir, norm2);
                float thinness = _Thinness;

                float diffuse = saturate(dot(norm, lightDir));
                float specular = pow(saturate(dot(norm, reflectDir)), 32);

                float scatter = _Scatter;
                float3 scatterDir = normalize(-lightDir + norm2 * scatter);
                float translucency = saturate(dot(viewDir, scatterDir)) * thinness;

                float3 amb = 0.05 * radiance;
                float3 spec = smoothmap.w * (diffuse * specular) * radiance;
                float3 diff = diffuse * radiance;

                float3 litColor = color * (amb + diff + spec);     
                litColor += color * translucencyRadiance * translucency;

                int perObjectLightIndex = 0;

                #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                    float4 lightPositionWS = _AdditionalLightsBuffer[perObjectLightIndex].position;
                    half3 lightColor = _AdditionalLightsBuffer[perObjectLightIndex].color.rgb;
                    half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[perObjectLightIndex].attenuation;
                    half4 spotDirection = _AdditionalLightsBuffer[perObjectLightIndex].spotDirection;
                    uint lightLayerMask = _AdditionalLightsBuffer[perObjectLightIndex].layerMask;
                #else
                    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
                    half3 lightColor = _AdditionalLightsColor[perObjectLightIndex].rgb;
                    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];
                    half4 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex];
                    uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[perObjectLightIndex]);
                #endif

                // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
                // This way the following code will work for both directional and punctual lights.
                float3 lightVector = lightPositionWS.xyz - i.worldPos * lightPositionWS.w;
                float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

                half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
                // full-float precision required on some platforms
                float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

                lightDir = lightDirection;
                radiance = lightColor * attenuation;
                translucencyRadiance = radiance * _SubsurfaceColor.xyz;

                reflectDir = reflect(lightDir, norm);

                diffuse = saturate(dot(norm, lightDir));
                specular = pow(saturate(dot(norm, reflectDir)), 32);

                scatterDir = normalize(-lightDir + norm * scatter);
                translucency = saturate(dot(viewDir, scatterDir)) * thinness;

                spec = smoothmap.w * (diffuse * specular) * radiance;
                diff = diffuse * radiance;
                litColor += color * (diff + spec) ;

                color = litColor;

                variance = variance * (1.0-i.worldUV.x);
                color = lerp(color, _Color0.xyz * _Color0.w, min(1.0, variance.y *.25 + variance.x *.25) );

                if(_VFX){

                    float mask = i.height;
                    color = lerp(color, color * _Color1, 1.0 - mask);
                    color = lerp(color, color * _Color0 * 0.5, pow((1.0 - mask), 6));
                    color = lerp(color * 0.125, color,  smoothstep(0.4, 1.,  i.height) + (1 - smoothstep(0.0, .1,   i.height)) );

                    float2 wUV = i.worldUV.xy;
                    float2 scale = float2(4.0, 4.0);
                    float2 offset = float2(time, time) * .5;
                    offset += (float2(i.flow.x , i.flow.y) * 2.0 - 1.0);
                    wUV = scale * wUV + offset;
                    float4 noise = SAMPLE_TEXTURE2D(_Noisemap, sampler_Noisemap, wUV);
                    float fireTrail = noise.x + noise.z;
                    fireTrail *= (1.0 - i.height);

                    float trailDist = repulsor.z * 1.0;
                    float fireMask = 1.0 - smoothstep(0.0, trailDist, (length(float2(repulsor.x, repulsor.y) - i.worldUV))/ 0.25);
                    fireMask *= (1.0 - i.height);

                    fireTrail *= fireMask;
                    fireTrail = lerp(fireTrail * 2.0, fireTrail, repulsor.z);

                    float3 fireColor =  float3(fireTrail * 4.0, fireTrail * 1., fireTrail * 0.25);
                    color.xyz = lerp(color.xyz, fireColor.xyz, fireTrail * repulsor.z );

                    wUV = i.worldUV.xy;
                    scale = float2(8.0, 8.0);
                    offset = float2(time, time) * 0.025;
                    offset += (float2(i.flow.x , i.flow.y) * 2.0 - 1.0);
                    wUV = scale * wUV + offset;
                    float4 noise2 = SAMPLE_TEXTURE2D(_Noisemap, sampler_Noisemap, wUV);
                    fireTrail = smoothstep(0.0, 0.1, (noise2.y * noise2.y * noise2.y));
                    fireTrail *= (1.0 - i.height);
                    float b = fireTrail;

                    trailDist = ( repulsor.z) * 1.0;
                    fireMask = 1.0 - smoothstep(0.0, trailDist, (length(float2(repulsor.x, repulsor.y) - i.worldUV))/  1.);
                    fireMask *= (1.0 - i.height);

                    float embersTrail =  fireTrail * ( 1.0 - i.height) *.25 + fireTrail * fireMask ;
                    fireTrail =  fireTrail * ( i.height) + fireTrail * fireMask ;
                    //fireTrail = lerp(fireTrail * 2.0, fireTrail, repulsor.z );

                    float embers =  embersTrail;
                    float fire = fireTrail;
                    color.xyz = lerp(color.xyz, float3(.75  , 0.125, 0.0) , embers);
                    color.xyz = lerp(color.xyz, float3(1.0 , 0.125, 0.0) , fire);
                    //color.xyz = lerp(color.xyz, float3(0.0, 0.0, 0.0), b * 5 * (1.0 - repulsor.z));
                }

                float mask = length(float2((i.worldPos.x / ((worldScale * 3.0)/2)), (i.worldPos.z / ((worldScale * 3.0)/2))));
                mask = 1.0 - smoothstep(0.2, 0.4, mask);
                //mask = 1.0 - smoothstep(0.3, 1, mask);
                color.xyz = lerp( float3(0.0, 0.0, 0.0), color.xyz, mask);

                if (a <= 0.2){
                    discard;
                }
                else{
                    fragout.col = float4(color.x, color.y, color.z, a);
                }
                return fragout;
            }
            ENDHLSL
        }


    }
}