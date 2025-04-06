using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

using static QuadstripUtil;

public class ProceduralRenderingFeature : ScriptableRendererFeature
{
    [SerializeField] private ProceduralDrawSettings settings;
    [SerializeField] private Material material;
    private ProceduralPass proceduralRenderPass;

    public override void Create() {
        if (material == null)
        {
            return;
        }
        proceduralRenderPass = new ProceduralPass(material, settings);
        proceduralRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderData) {
        if ((renderData.cameraData.cameraType == CameraType.Game) || (renderData.cameraData.cameraType == CameraType.SceneView))
        {
            renderer.EnqueuePass(proceduralRenderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        proceduralRenderPass.DisposeResources();
        #if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
            }
            else
            {
                //DestroyImmediate(material);
            }
        #else
                Destroy(material);
        #endif
    }
}

[Serializable]
public class ProceduralDrawSettings
{
    [SerializeField] public Vector3 VolumeBounds;  
    [SerializeField] public Mesh mesh;
    [SerializeField] public uint2 meshesPerInstance;
    [SerializeField] public uint2 instanceCount;
    [SerializeField] public Vector4 seed;
    [SerializeField] public bool enableWind;
    [SerializeField] public Vector4 wind;
    [SerializeField] public Vector4 repulsor;
}

class ProceduralPass : ScriptableRenderPass
{
    private ProceduralDrawSettings settings;
    private Material material;
    private Material copyMaterial;
    private Vector4 instanceScale;
    private Vector4 repulsor;
    private float minimumModelHeight;
    private Matrix4x4 viewProjMatrix;

    private int modelSizeUID;
    private int modelIndicesSizeUID;
    private int meshCountXUID;
    private int meshCountYUID;
    private int meshCountZUID;
    private int2 spawnerScaleUID;
    private int timeUID;
    private int windUID;
    private int seedUID;
    private int repulsorUID;
    private int UID;
    private int2 instanceCountUID;
    private int instanceScaleUID;
    private int submeshCountXUID;
    private int submeshCountYUID;
    private int camPosUID;
    private Vector4 offsetID;

    private ComputeBuffer modelPosBuffer;
    private int modelPosBufferUID;
    private ComputeBuffer modelNormBuffer;
    private int modelNormBufferUID;
    private ComputeBuffer modelUVBuffer;
    private int modelUVBufferUID;
    private ComputeBuffer modelIndexBuffer;
    private int modelIndexBufferUID;
    private ComputeBuffer emitterBuffer;
    private int emitterBufferUID;

    private ComputeBuffer curveBuffer;
    private int curveBufferUID;
    private ComputeBuffer vertexBuffer;
    private int vertexBufferUID;
    private ComputeBuffer normalBuffer;
    private int normalBufferUID;
    private ComputeBuffer uvBuffer;
    private int uvBufferUID;
    private GraphicsBuffer indexBuffer;
    private int indexBufferUID;
    private ComputeBuffer indirectBuffer;
    private int indirectBufferUID;
    private ComputeBuffer positionsBuffer;
    private int positionsBufferUID;
    private ComputeBuffer culledPositionsBuffer;
    private int culledPositionsBufferUID;
    private ComputeBuffer directionBuffer;
    private int directionBufferUID;

    private int normalmapR32UID;
    private int normalmapUID;
    private int heightmapHistoryUID;
    private int viewProjMatrixUID;

    private ComputeShader computeShader;
    private int[] kernelIDs;

    private QuadstripUtil.Quadstrip quadstrip;
    private QuadstripUtil.shape2D shape2d;

    private int workgroupCountX;
    private int workgroupCountY;
    private int workgroupCountZ;

    private int workgroupCountX_1;
    private int workgroupCountY_1;
    private int workgroupCountZ_1;

    private int workgroupCountX_2;
    private int workgroupCountY_2;
    private int workgroupCountZ_2;

    private int submeshCountX;
    private int submeshCountY;

    private Vector3 camPos;

    struct vert
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
    }
    private vert[] modelData;
    private uint modelSize;
    private int[] modelIndicesData;
    private uint modelIndicesSize;

    private Bounds bounds;
    private Vector3[] emitterData;

    private Matrix4x4[] trnsfrm;

    private LocalKeyword NOISE;
    private LocalKeyword VFX;
    private LocalKeyword WIND;
    private LocalKeyword REPULSE;
    private LocalKeyword CUSTOMMESH;
    private LocalKeyword ALPHATEST;

    private bool updateVertexBuffer;

    private RenderTexture nmap;
    private RenderTexture nmap_2;

    public ProceduralPass(Material material, ProceduralDrawSettings settings)
    {
        this.material = material;
        this.copyMaterial = new Material((Shader)Resources.Load("CopyShader"));
        this.settings = settings;
        Initialize();
    }

    public void Initialize()
    {
        InputManagerUtil.WorldScale = settings.VolumeBounds.x;

        computeShader = (ComputeShader)Resources.Load("ComputeParticleDeformation");

        settings.meshesPerInstance[0] = Math.Max(1, settings.meshesPerInstance[0]);
        settings.meshesPerInstance[1] = Math.Max(1, settings.meshesPerInstance[1]);
        uint meshCount = (uint)(settings.meshesPerInstance[0] * settings.meshesPerInstance[1]);

        instanceScale = new Vector4(settings.VolumeBounds.x / (float)settings.instanceCount[0], settings.VolumeBounds.y, settings.VolumeBounds.z / (float)settings.instanceCount[1], 1.0f);

        Vector3 origin = new Vector3(0, 0, 0);
        Vector3 scale = new Vector3(instanceScale.x, instanceScale.y, instanceScale.z);
        Vector3 margin = new Vector3(scale.x / settings.meshesPerInstance[0], 1.0f, scale.z / settings.meshesPerInstance[1]);

        emitterData = new Vector3[3]{
            origin,
            scale,
            margin
        };

        bounds = new Bounds(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1000.0f, 1000.0f, 1000.0f));

        Vector3[] quadBezier = new[] { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.5f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f) };
        shape2d = QuadstripUtil.defineShape2D(0.0f, new Vector3(0.0f, 0.0f, -1.0f));
        QuadstripUtil.curvePoint[] curvePoints = QuadstripUtil.sampleBezier(quadBezier, 10);

        if (!settings.mesh)
        {
            quadstrip = QuadstripUtil.loft(shape2d, curvePoints);
            modelSize = (uint)quadstrip.positions.Length;
            modelData = new vert[modelSize];
            for (int i = 0; i < modelSize; i++)
            {
                modelData[i].position = quadstrip.positions[i];
                modelData[i].normal = new Vector3(0.0f, 1.0f, 0.0f);
                modelData[i].uv = quadstrip.uvs[i];
            }

            modelIndicesSize = (uint)quadstrip.indices.Length;
            modelIndicesData = new int[modelIndicesSize];
            for (int i = 0; i < modelIndicesSize; i++)
            {
                modelIndicesData[i] = quadstrip.indices[i];
            }
        }
        else
        {
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(settings.mesh))
            {
                var data = dataArray[0];
                var positions = new NativeArray<Vector3>(settings.mesh.vertexCount, Allocator.TempJob);
                var normals = new NativeArray<Vector3>(settings.mesh.vertexCount, Allocator.TempJob);
                var uvs = new NativeArray<Vector2>(settings.mesh.vertexCount, Allocator.TempJob);
                var indices = new NativeArray<int>((int)settings.mesh.GetIndexCount(0), Allocator.TempJob);
                data.GetVertices(positions);
                data.GetNormals(normals);
                data.GetUVs(0, uvs);
                data.GetIndices(indices, 0);

                modelSize = (uint)settings.mesh.vertexCount;
                modelData = new vert[modelSize];
                for (int i = 0; i < modelSize; i++)
                {
                    modelData[i].position = positions[i];
                    modelData[i].normal = normals[i];
                    modelData[i].uv = uvs[i];
                }

                modelIndicesSize = (uint)indices.Length;
                modelIndicesData = new int[modelIndicesSize];
                for (int i = 0; i < modelIndicesSize; i++)
                {
                    modelIndicesData[i] = indices[i];
                }
                positions.Dispose();
                uvs.Dispose();
                indices.Dispose();
            }
        }

        Vector3[] modelVertData = new Vector3[modelSize];
        for (int i = 0; i < modelSize; i++)
        {
            minimumModelHeight = Mathf.Max(modelData[i].position.y, settings.VolumeBounds.y);
            modelVertData[i] = modelData[i].position;
        };
        emitterData[1].y = minimumModelHeight;
        Vector3[] modelNormData = new Vector3[modelSize];
        for (int i = 0; i < modelSize; i++)
        {
            modelNormData[i] = modelData[i].normal;
        };
        Vector2[] modelUVData = new Vector2[modelSize];
        for (int i = 0; i < modelSize; i++)
        {
            modelUVData[i] = modelData[i].uv;
        };
        
        int[] modelIndexData = new int[modelIndicesSize];
        for (int i = 0; i < modelIndicesSize; i++)
        {
            modelIndexData[i] = modelIndicesData[i];
        };

        List<Vector3> vertexData = new List<Vector3>();
        List<Vector3> normalData = new List<Vector3>();
        List<Vector2> uvData = new List<Vector2>();
        List<int> indexData = new List<int>();
        List<Vector3> curveData = new List<Vector3>();
        for (int i = 0; i < meshCount; i++)
        {
            for (int j = 0; j < modelSize; j++)
            {
                vertexData.Add(modelData[j].position);
                normalData.Add(modelData[j].normal);
                uvData.Add(modelData[j].uv);
            }

            for (int j = 0; j < modelIndicesSize; j++)
            {
                indexData.Add(i * (int)modelSize + modelIndicesData[j]);
            }

            curveData.Add(new Vector3(0.0f, 0.0f, 0.0f));
            curveData.Add(new Vector3(0.0f, 0.5f, 0.0f));
            curveData.Add(new Vector3(0.0f, 1.0f, 0.0f));
        }

        uint vertexCount = modelSize * meshCount;
        uint indexCount = modelIndicesSize * meshCount;
        uint cpCount = 3 * meshCount;

        // Create GPU buffers
        emitterBuffer = new ComputeBuffer(3, sizeof(float) * 3, ComputeBufferType.Default);
        emitterBuffer.name = "particle emitter buffer";
        modelPosBuffer = new ComputeBuffer((int)modelSize, sizeof(float) * 3, ComputeBufferType.Default);
        modelPosBuffer.name = "particle model buffer - pos";
        modelNormBuffer = new ComputeBuffer((int)modelSize, sizeof(float) * 3, ComputeBufferType.Default);
        modelNormBuffer.name = "particle model buffer - norm";
        modelUVBuffer = new ComputeBuffer((int)modelSize, sizeof(float) * 2, ComputeBufferType.Default);
        modelUVBuffer.name = "particle model buffer - uv";
        modelIndexBuffer = new ComputeBuffer((int)modelIndicesSize, sizeof(int), ComputeBufferType.Default);
        modelIndexBuffer.name = "particle mmodel buffer - index";

        curveBuffer = new ComputeBuffer((int)cpCount, sizeof(float) * 3, ComputeBufferType.Default);
        curveBuffer.name = "particle curve buffer";
        vertexBuffer = new ComputeBuffer((int)vertexCount, sizeof(float) * 3, ComputeBufferType.Default);
        vertexBuffer.name = "vertex buffer";
        normalBuffer = new ComputeBuffer((int)vertexCount, sizeof(float) * 3, ComputeBufferType.Default);
        normalBuffer.name = "Normal buffer";
        uvBuffer = new ComputeBuffer((int)vertexCount, sizeof(float) * 2, ComputeBufferType.Default);
        uvBuffer.name = "uv buffer";
        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Index, (int)indexCount, sizeof(int));
        indexBuffer.name = "indices buffer";
        indirectBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments | ComputeBufferType.Structured);
        indirectBuffer.name = "indirect buffer";
        positionsBuffer = new ComputeBuffer((int)(settings.instanceCount[0] * settings.instanceCount[1]), sizeof(float) * 4, ComputeBufferType.Default);
        positionsBuffer.name = "particle positions buffer";
        culledPositionsBuffer = new ComputeBuffer((int)(settings.instanceCount[0] * settings.instanceCount[1]), sizeof(float) * 4, ComputeBufferType.Append | ComputeBufferType.Structured);
        culledPositionsBuffer.name = "culled particle positions buffer";
        directionBuffer = new ComputeBuffer((int)(settings.instanceCount[0] * settings.instanceCount[1] * meshCount), sizeof(float) * 3, ComputeBufferType.Default);
        directionBuffer.name = "direction buffer";

        Vector4[] instancePositions = new Vector4[settings.instanceCount[0] * settings.instanceCount[1]];
        Vector4[] culledPositions = new Vector4[settings.instanceCount[0] * settings.instanceCount[1]];
        Vector3[] directions = new Vector3[settings.instanceCount[0] * settings.instanceCount[1] * meshCount];

        float w = settings.instanceCount[0] * instanceScale.x;
        float startX = -w * 0.5f + instanceScale.x * 0.5f;
        float h = settings.instanceCount[1] * instanceScale.z;
        float startY = -h * 0.5f + instanceScale.z * 0.5f;

        for (int i = 0; i < settings.instanceCount[0]; i++)
        {
            for (int j = 0; j < settings.instanceCount[1]; j++)
            {
                instancePositions[i * settings.instanceCount[1] + j] = new Vector4(startX + instanceScale.x * i, 0.0f, startY + instanceScale.z * j, 1.0f);
                culledPositions[i * settings.instanceCount[1] + j] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            }
        }

        submeshCountX = (int) (settings.instanceCount[0] * settings.meshesPerInstance[0]);
        submeshCountY = (int) (settings.instanceCount[1] * settings.meshesPerInstance[1]);

        positionsBuffer.SetData(instancePositions);
        culledPositionsBuffer.SetData(culledPositions);
        directionBuffer.SetData(directions);

        emitterBuffer.SetData(emitterData);
        modelPosBuffer.SetData(modelVertData);
        modelNormBuffer.SetData(modelNormData);
        modelUVBuffer.SetData(modelUVData);
        modelIndexBuffer.SetData(modelIndexData);

        curveBuffer.SetData(curveData);
        vertexBuffer.SetData(vertexData);
        normalBuffer.SetData(normalData);
        uvBuffer.SetData(uvData);
        indexBuffer.SetData(indexData);
        uint[] args = { modelIndicesSize * meshCount, (uint)(settings.instanceCount[0] * settings.instanceCount[1]), 0, 0, 0 };
        indirectBuffer.SetData(args);

        kernelIDs = new int[6];
        kernelIDs[0] = computeShader.FindKernel("ComputeCurves");
        kernelIDs[1] = computeShader.FindKernel("ComputeGeometry");
        kernelIDs[2] = computeShader.FindKernel("ComputeCulling");
        kernelIDs[3] = computeShader.FindKernel("ComputeDirections");
        kernelIDs[4] = computeShader.FindKernel("ComputeClear");

        uint workgroupSizeX;
        uint workgroupSizeY;
        uint workgroupSizeZ;
        computeShader.GetKernelThreadGroupSizes(kernelIDs[0], out workgroupSizeX, out workgroupSizeY, out workgroupSizeZ);
        workgroupCountX = (int)(Mathf.Ceil(settings.meshesPerInstance[0] / workgroupSizeX));
        workgroupCountY = (int)(Mathf.Ceil(settings.meshesPerInstance[1] / workgroupSizeY));
        workgroupCountZ = (int)(Mathf.Ceil(1 / workgroupSizeZ));

        uint workgroupSizeX_1;
        uint workgroupSizeY_1;
        uint workgroupSizeZ_1;
        computeShader.GetKernelThreadGroupSizes(kernelIDs[2], out workgroupSizeX_1, out workgroupSizeY_1, out workgroupSizeZ_1);
        workgroupCountX_1 = (int)(Mathf.Ceil(settings.instanceCount[0] / workgroupSizeX_1));
        workgroupCountY_1 = (int)(Mathf.Ceil(settings.instanceCount[1] / workgroupSizeY_1));
        workgroupCountZ_1 = (int)(Mathf.Ceil(1 / workgroupSizeZ_1));

        uint workgroupSizeX_2;
        uint workgroupSizeY_2;
        uint workgroupSizeZ_2;
        computeShader.GetKernelThreadGroupSizes(kernelIDs[3], out workgroupSizeX_2, out workgroupSizeY_2, out workgroupSizeZ_2);
        workgroupCountX_2 = (int)(Mathf.Ceil(submeshCountX / workgroupSizeX_2));
        workgroupCountY_2 = (int)(Mathf.Ceil(submeshCountY / workgroupSizeY_2));
        workgroupCountZ_2 = (int)(Mathf.Ceil(1 / workgroupSizeZ_2));

        // heigtnmap
        nmap = new RenderTexture(submeshCountX, submeshCountY, 0);
        nmap.enableRandomWrite = true;
        nmap.graphicsFormat = GraphicsFormat.R32_SFloat;
        nmap.Create();

        nmap_2 = new RenderTexture(submeshCountX, submeshCountY, 0);
        nmap_2.enableRandomWrite = false;
        nmap_2.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        nmap_2.Create();


        repulsor = settings.repulsor;

        // retrieve shader resource UIDs for buffers
        emitterBufferUID = Shader.PropertyToID("emitterBuffer");
        modelPosBufferUID = Shader.PropertyToID("modelPosBuffer");
        modelNormBufferUID = Shader.PropertyToID("modelNormBuffer");
        modelUVBufferUID = Shader.PropertyToID("modelUVBuffer");
        modelIndexBufferUID = Shader.PropertyToID("modelIndexBuffer");

        curveBufferUID = Shader.PropertyToID("curveBuffer");
        vertexBufferUID = Shader.PropertyToID("vertexBuffer");
        normalBufferUID = Shader.PropertyToID("normalBuffer");
        uvBufferUID = Shader.PropertyToID("uvBuffer");
        indexBufferUID = Shader.PropertyToID("indexBuffer");
        positionsBufferUID = Shader.PropertyToID("positionsBuffer");
        culledPositionsBufferUID = Shader.PropertyToID("culledPositionsBuffer");
        normalmapR32UID = Shader.PropertyToID("_NormalmapR32");
        normalmapUID = Shader.PropertyToID("_Normalmap");

        heightmapHistoryUID = Shader.PropertyToID("history");
        directionBufferUID = Shader.PropertyToID("directionBuffer");
        camPosUID = Shader.PropertyToID("camPos");

        // restrieve shader resource UIDs for constants
        indirectBufferUID = Shader.PropertyToID("indirectBuffer");
        meshCountXUID = Shader.PropertyToID("particleCountX");
        meshCountYUID = Shader.PropertyToID("particleCountY");
        meshCountZUID = Shader.PropertyToID("particleCountZ");
        modelSizeUID = Shader.PropertyToID("modelSize");
        modelIndicesSizeUID = Shader.PropertyToID("modelIndicesSize");
        submeshCountXUID = Shader.PropertyToID("submeshCountX");
        submeshCountYUID = Shader.PropertyToID("submeshCountY");

        timeUID = Shader.PropertyToID("time");
        windUID = Shader.PropertyToID("wind");
        seedUID = Shader.PropertyToID("seed");
        repulsorUID = Shader.PropertyToID("repulsor");
        spawnerScaleUID = new int2();
        spawnerScaleUID[0] = Shader.PropertyToID("modelHeight");
        spawnerScaleUID[1] = Shader.PropertyToID("worldScale");
        viewProjMatrixUID = Shader.PropertyToID("viewProjMat");

        instanceCountUID = new int2();
        instanceCountUID[0] = Shader.PropertyToID("spawnersCountX");
        instanceCountUID[1] = Shader.PropertyToID("spawnersCountY");
        instanceScaleUID = Shader.PropertyToID("spawnerScale");

        trnsfrm = new Matrix4x4[7];
        trnsfrm[0] = new Matrix4x4();
        trnsfrm[1] = new Matrix4x4();
        trnsfrm[2] = new Matrix4x4();
        trnsfrm[3] = new Matrix4x4();
        trnsfrm[4] = new Matrix4x4();
        trnsfrm[5] = new Matrix4x4();
        trnsfrm[6] = new Matrix4x4();

        trnsfrm[1].SetRow(0, new Vector4(1.0f, 0.0f, 0.0f, settings.VolumeBounds.x));
        trnsfrm[2].SetRow(0, new Vector4(1.0f, 0.0f, 0.0f, -settings.VolumeBounds.x));
        trnsfrm[3].SetRow(2, new Vector4(0.0f, 0.0f, 1.0f, settings.VolumeBounds.x));
        trnsfrm[4].SetRow(2, new Vector4(0.0f, 0.0f, 1.0f, -settings.VolumeBounds.x));
        trnsfrm[5].SetRow(0, new Vector4(0.0f, 0.0f, 1.0f, -settings.VolumeBounds.x));
        trnsfrm[5].SetRow(2, new Vector4(0.0f, 0.0f, 1.0f, settings.VolumeBounds.x)); 
        trnsfrm[6].SetRow(0, new Vector4(0.0f, 0.0f, 1.0f, -settings.VolumeBounds.x));
        trnsfrm[6].SetRow(2, new Vector4(0.0f, 0.0f, 1.0f, -settings.VolumeBounds.x));

        NOISE = new LocalKeyword(material.shader, "_NOISEMAP");
        VFX = new LocalKeyword(material.shader, "_VFX");

        UpdateVertexBuffer();
        ClearRenderTargets();
    }

    private void UpdateVertexBuffer()
    {

        // Bind and update shader constants
        computeShader.SetInt(meshCountXUID, (int)settings.meshesPerInstance[0]);
        computeShader.SetInt(meshCountYUID, (int)settings.meshesPerInstance[1]);
        computeShader.SetInt(meshCountZUID, 1);
        computeShader.SetInt(modelSizeUID, (int)modelSize);
        computeShader.SetInt(modelIndicesSizeUID, (int)modelIndicesSize);
        computeShader.SetVector(seedUID, settings.seed);
        computeShader.SetFloat(spawnerScaleUID[0], minimumModelHeight);

        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[0], emitterBufferUID, emitterBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[0], curveBufferUID, curveBuffer);
        // Dispatch (compute curves)
        computeShader.Dispatch(kernelIDs[0], workgroupCountX, workgroupCountY, workgroupCountZ);

        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[1], emitterBufferUID, emitterBuffer);
        computeShader.SetBuffer(kernelIDs[1], curveBufferUID, curveBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelPosBufferUID, modelPosBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelNormBufferUID, modelNormBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelUVBufferUID, modelUVBuffer);
        computeShader.SetBuffer(kernelIDs[1], modelIndexBufferUID, modelIndexBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[1], vertexBufferUID, vertexBuffer);
        computeShader.SetBuffer(kernelIDs[1], normalBufferUID, normalBuffer);
        computeShader.SetBuffer(kernelIDs[1], uvBufferUID, uvBuffer);
        computeShader.SetBuffer(kernelIDs[1], indexBufferUID, indexBuffer);
        // Dispatch (compute geometry)
        computeShader.Dispatch(kernelIDs[1], workgroupCountX, workgroupCountY, workgroupCountZ);
    }

    private void ClearRenderTargets()
    {
        computeShader.SetTexture(kernelIDs[4], normalmapR32UID, nmap);
        computeShader.Dispatch(kernelIDs[4], workgroupCountX_2, workgroupCountY_2, workgroupCountZ_2);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderData)
    {
        CommandBuffer cmdbuffer = CommandBufferPool.Get("ProceduralDraw");

        // 1. Dispatch (compute instance positions and culling)
        // ----------------------------------------------------------
        culledPositionsBuffer.SetCounterValue(0);
        // Set uniforms
        computeShader.SetInt(instanceCountUID[0], (int)settings.instanceCount[0]);
        viewProjMatrix = Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix;
        computeShader.SetMatrix(viewProjMatrixUID, viewProjMatrix);
        computeShader.SetVector(instanceScaleUID, instanceScale);
        computeShader.SetFloat(spawnerScaleUID[0],  minimumModelHeight);
        // Set buffers
        // ---------------- read -----------------------------------
        computeShader.SetBuffer(kernelIDs[2], positionsBufferUID, positionsBuffer);
        // ---------------- write -----------------------------------
        computeShader.SetBuffer(kernelIDs[2], culledPositionsBufferUID, culledPositionsBuffer);

        cmdbuffer.DispatchCompute(computeShader, kernelIDs[2], workgroupCountX_1, workgroupCountY_1, workgroupCountZ_1);
        cmdbuffer.CopyCounterValue(culledPositionsBuffer, indirectBuffer, 4);

        if(InputManagerUtil.Active == true)
        {
            // 2. Dispatch (compute directions)
            // ----------------------------------------------------------
            // Set uniforms
            computeShader.SetInt(submeshCountXUID, (int)submeshCountX);
            computeShader.SetInt(submeshCountYUID, (int)submeshCountY);
            computeShader.SetInt(instanceCountUID[0], (int)settings.instanceCount[0]);
            computeShader.SetInt(instanceCountUID[1], (int)settings.instanceCount[1]);
            computeShader.SetFloat("clear", InputManagerUtil.Clear ? 1.0f : 0.0f);
            computeShader.SetVector(repulsorUID, new Vector4(InputManagerUtil.Coords.x, InputManagerUtil.Coords.y, InputManagerUtil.Coords.z, InputManagerUtil.Coords.w));
            // Set Buffers
            // ----------------- read / write ---------------------------------------
            computeShader.SetTexture(kernelIDs[3], normalmapR32UID, nmap);
            
            cmdbuffer.DispatchCompute(computeShader, kernelIDs[3], workgroupCountX_2, workgroupCountY_2, workgroupCountZ_2);
            InputManagerUtil.Active = false;
        }

        RenderTexture currentActiveRT = RenderTexture.active;
        Graphics.Blit(nmap, nmap_2, copyMaterial, -1);
        RenderTexture.active = currentActiveRT;

        // 3. Draw procedural
        // ----------------------------------------------------------
        bounds = new Bounds(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1000.0f, 1000.0f, 1000.0f));

        // Set keywords
        material.EnableKeyword(NOISE);
        material.EnableKeyword(VFX);   

        // Set buffers 
        material.SetBuffer(vertexBufferUID, vertexBuffer);
        material.SetBuffer(normalBufferUID, normalBuffer);
        material.SetBuffer(uvBufferUID, uvBuffer);
        material.SetBuffer(culledPositionsBufferUID, culledPositionsBuffer);
        material.SetTexture(normalmapUID, nmap_2);

        // Set uniforms
        material.SetFloat(spawnerScaleUID[0], minimumModelHeight);
        material.SetFloat(spawnerScaleUID[1], settings.VolumeBounds.x);
        material.SetFloat(timeUID, Time.time);
        material.SetVector(seedUID, settings.seed);
        material.SetInt(instanceCountUID[0], (int)settings.instanceCount[0]);
        material.SetVector(camPosUID, Camera.main.transform.position);
        material.SetVector(repulsorUID, new Vector4(InputManagerUtil.ActiveCoords.x, InputManagerUtil.ActiveCoords.y, InputManagerUtil.ActiveCoords.z, 0.0f));
        material.SetVector(windUID, settings.wind); 

        cmdbuffer.DrawProceduralIndirect(indexBuffer, trnsfrm[0], material, 0 , MeshTopology.Triangles, indirectBuffer);

        context.ExecuteCommandBuffer(cmdbuffer);
        context.Submit();
    }
    
    public void DisposeResources()
    {
        if(modelPosBuffer != null) {
            modelPosBuffer.Release();
        }

        if (modelUVBuffer != null) {
            modelUVBuffer.Release();
        }

        if (modelIndexBuffer != null) {
            modelIndexBuffer.Release();
        }
        
        if (emitterBuffer != null) {
            emitterBuffer.Release();
        }
        
        if (curveBuffer != null) {
            curveBuffer.Release();
        }
        
        if (vertexBuffer != null) {
            vertexBuffer.Release();
        }
        
        if (uvBuffer != null) {
            uvBuffer.Release();
        }

        if (indexBuffer != null) {
            indexBuffer.Release();
        } 

        if (indirectBuffer != null){
            indirectBuffer.Release();    
        }

        if (positionsBuffer != null){
            positionsBuffer.Release();
        }

        if (culledPositionsBuffer != null){
            culledPositionsBuffer.Release();
        }

        if (nmap != null){
            nmap.Release();
        }

    }
}