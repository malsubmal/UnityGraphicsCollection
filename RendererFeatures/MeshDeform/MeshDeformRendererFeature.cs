using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Test
{using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
//The basic flow:
//Init:
//We find the gameobject with the mesh to deform
//save the mesh + mesh filter reference
    
//record the computeshader to render graph (1st pass)
//read back from the computeshader output buffer (2nd pass)
//reconstruct the mesh
//feedback to the mesh filter reference to render as per normal!
public class MeshDeformRendererFeature : ScriptableRendererFeature
{
    //Feature Param
    ExecuteMeshDeformPass _mMeshDeformPassComputePass;
    public RenderPassEvent RenderEvent;
    
    public MeshFilter DeformMeshFilter;
    public Mesh OriginalMesh;
    
    public Mesh DeformedMesh;

    public ComputeShader ComputeShader;
    //Pass Param
    public Vector4 CenterOffset;
    public float Radius;
    [Range(0.0f, 1.0f)] public float LerpValue;
    
    


    public override void Create()
    {
        _mMeshDeformPassComputePass = new ExecuteMeshDeformPass
        {
            renderPassEvent = RenderEvent
        };

        if (OriginalMesh != null && DeformedMesh != null)
            return;
        
        InitializeRenderFeature();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (DeformedMesh is null)
            InitializeRenderFeature();
        
        if (!SystemInfo.supportsComputeShaders)
        {
            return;
        }
        
        // Skip the render pass if the compute shader is null.
        if (ComputeShader == null)
        {
            return;
        }
        _mMeshDeformPassComputePass.Setup(this, OnMeshDeformed);
        renderer.EnqueuePass(_mMeshDeformPassComputePass);
    }

    private bool InitializeRenderFeature()
    {
        if (!OriginalMesh || !DeformMeshFilter)
        {
            var go = GameObject.Find("Deform");

            if (!go || !go.TryGetComponent(out MeshFilter mf) || !mf.sharedMesh)
            {
                Debug.LogError("Cannot find deform gameobject");
                return false;
            }

            DeformMeshFilter = mf;
            OriginalMesh = mf.sharedMesh;
        }

        DeformedMesh = DuplicateMesh(OriginalMesh);
        DeformMeshFilter.sharedMesh = DeformedMesh;
        
        return true;
    }

    private void OnDisable()
    {
        if (_mMeshDeformPassComputePass is not null)
            _mMeshDeformPassComputePass.Cleanup();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _mMeshDeformPassComputePass.Cleanup();
    }

    static Mesh DuplicateMesh(Mesh mesh)
    {
        Mesh newmesh = new Mesh();
        newmesh.name = "Temp Mesh Dup";
        newmesh.vertices = mesh.vertices;
        newmesh.triangles = mesh.triangles;
        newmesh.uv = mesh.uv;
        newmesh.normals = mesh.normals;
        newmesh.colors = mesh.colors;
        newmesh.tangents = mesh.tangents;
        newmesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        return newmesh;
    }

    private void OnMeshDeformed(NativeArray<float3> newMeshVertices)
    {
        DeformedMesh.vertices = newMeshVertices.Select(fl3 => new Vector3(fl3.x, fl3.y, fl3.z)).ToArray();
        DeformMeshFilter.sharedMesh = DeformedMesh;
        
        newMeshVertices.Dispose();
    }
}


public class ExecuteMeshDeformPass : ScriptableRenderPass
    {
        ComputeShader mComputeShader;
        BufferHandle mInputBufferHandle;
        BufferHandle mOutputBufferHandle;
        GraphicsBuffer mVertexBuffer;

        private int mPosOffset;
        private int mStride;

        MeshDeformRendererFeature mRendFeature;
        Action<NativeArray<float3>> mCallbackOnMeshDeformed;
        
        public List<Vector3> verticesPos;
        
        public Mesh outDeformedMesh;

        public void Setup(MeshDeformRendererFeature rendFeature, Action<NativeArray<float3>> callback)
        {
            verticesPos ??= new();
            mRendFeature = rendFeature;
            mComputeShader = rendFeature.ComputeShader;
            mCallbackOnMeshDeformed = callback;

            if (rendFeature.DeformedMesh is null)
            {
                Debug.LogError("Null Deformed Mesh");
                return;
            }
            
            mVertexBuffer = rendFeature.DeformedMesh.GetVertexBuffer(0);
            mPosOffset = rendFeature.DeformedMesh.GetVertexAttributeOffset(VertexAttribute.Position);
            mStride = rendFeature.DeformedMesh.GetVertexBufferStride(0);
            
        }

        public void Cleanup()
        {
            if (mVertexBuffer is not null)
                mVertexBuffer.Release();
        }
        
        // private class ReadbackPassData
        // {
        //     public BufferHandle readbackBufferHandle;
        //     public Action<NativeArray<float3>> onFetchBuffer;
        // }
        
        private class DeformPassData
        {
            public ComputeShader computeShader;

            public List<Vector3> originalVertexPosition;
            public BufferHandle inputBufferHandle;
            public BufferHandle outputBufferHandle;
            
            public Vector4 pCenterOffset;
            public float pRadius;
            public float pLerpValue;

            public int BufferOffset;
            public int BufferStride;
        }

        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(DeformPassData data, ComputeGraphContext cgContext)
        {
            
//            Debug.Log($"Lerp Val {data.pLerpValue} Radius {data.pRadius}");
            var kernelId = data.computeShader.FindKernel("MeshDeform");
            
            //bind buffers
            cgContext.cmd.SetBufferData(data.inputBufferHandle, data.originalVertexPosition);
            cgContext.cmd.SetComputeBufferParam(data.computeShader, kernelId, "OriginalMeshVertexPosition", data.inputBufferHandle);
            cgContext.cmd.SetComputeBufferParam(data.computeShader, kernelId, "UpdatedMeshVertexPosition", data.outputBufferHandle);

            //bind params
            cgContext.cmd.SetComputeIntParam(data.computeShader, "PosOffset", data.BufferOffset);
            cgContext.cmd.SetComputeIntParam(data.computeShader, "VertexBufferStride", data.BufferStride);
            
            cgContext.cmd.SetComputeVectorParam(data.computeShader, "CenterOffset", data.pCenterOffset);
            cgContext.cmd.SetComputeFloatParam(data.computeShader, "Radius", data.pRadius);
            
            cgContext.cmd.SetComputeFloatParam(data.computeShader, "LerpValue", data.pLerpValue);
            cgContext.cmd.SetComputeIntParam(data.computeShader, "VertexCount", data.originalVertexPosition.Count);
            
            data.computeShader.GetKernelThreadGroupSizes(kernelId, out uint sizeX,out _,out _);
            cgContext.cmd.DispatchCompute(data.computeShader, kernelId, (int)((data.originalVertexPosition.Count / sizeX) + 1), 1, 1);
        }
        void CreateBufferHandles(RenderGraph renderGraph, int countVertex)
        {
            var bufferDesc = new BufferDesc()
            {
                name = "InputBuffer",
                count = countVertex,
                stride = sizeof(float) * 3,
                target = GraphicsBuffer.Target.Structured
            };
            mInputBufferHandle = renderGraph.CreateBuffer(bufferDesc);
            
            // bufferDesc.name = "OutputBuffer";
            // bufferDesc.stride = stride;
            // mOutputBufferHandle = renderGraph.CreateBuffer(bufferDesc);

        }
        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string computePassName = "[Custom Pass] Compute Mesh Deform";
            const string readbackPassName = "[Custom Pass] Readback Compute Data";

            mRendFeature.OriginalMesh.GetVertices(verticesPos);
            CreateBufferHandles(renderGraph, verticesPos.Count);
            var outBufferHandle = renderGraph.ImportBuffer(mVertexBuffer);
            
            using (var computePassBuilder =
                   renderGraph.AddComputePass(computePassName, out DeformPassData deformPassData))
            {
//                Debug.Log($"Fill Data lerp val {mRendFeature.LerpValue} rad {mRendFeature.Radius}");
                //fill pass data
                deformPassData.inputBufferHandle = mInputBufferHandle;
                deformPassData.outputBufferHandle = outBufferHandle;
                deformPassData.originalVertexPosition = verticesPos;
                deformPassData.computeShader = mComputeShader;
                deformPassData.pCenterOffset = mRendFeature.CenterOffset;
                deformPassData.pLerpValue = mRendFeature.LerpValue;
                deformPassData.pRadius = mRendFeature.Radius;
                deformPassData.BufferOffset = mPosOffset;
                deformPassData.BufferStride = mStride;
                
                computePassBuilder.UseBuffer(deformPassData.inputBufferHandle, AccessFlags.Read);
                computePassBuilder.UseBuffer(deformPassData.outputBufferHandle, AccessFlags.ReadWrite);
                
                computePassBuilder.SetRenderFunc(static (DeformPassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
            }
            
            // using (var builder = renderGraph.AddUnsafePass(readbackPassName, out ReadbackPassData passData))
            // {
            //     builder.AllowPassCulling(false);
            //
            //     passData.readbackBufferHandle = mOutputBufferHandle;
            //     passData.onFetchBuffer = mCallbackOnMeshDeformed;
            //     builder.UseBuffer(passData.readbackBufferHandle);
            //     builder.SetRenderFunc(static (ReadbackPassData data, UnsafeGraphContext ctx) =>
            //     {
            //         ctx.cmd.RequestAsyncReadback(data.readbackBufferHandle, request =>
            //         {
            //             var result = request.GetData<float3>();
            //             data.onFetchBuffer(result);
            //         });
            //     });
            // }
        }
    }

    
}