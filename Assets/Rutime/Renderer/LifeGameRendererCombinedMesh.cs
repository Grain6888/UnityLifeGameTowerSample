using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeGame3D
{
    public interface ILifeGameRenderer : IDisposable
    {
        void AddRenderBuffer(DataChunk input, int height);
        void RenderInstance(Bounds bounds);
    }

    /// <summary>
    /// メッシュを結合して描画する
    /// </summary>
    public class LifeGameRendererCombinedMesh : ILifeGameRenderer
    {
        private readonly MaterialPropertyBlock _mpb;
        private GraphicsBuffer _gb;

        private NativeList<uint> _renderBuffers;

        private readonly uint[] _indirectArgs = new uint[]
        {
            0u, 0u, 0u, 0u, 0u
        };
        private readonly GraphicsBuffer _argsBuffer;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly RenderParams _renderParams;

        private LifeGameUtil.LifeGameMesh _lifeGameMesh;
        private readonly Material _material;
        private readonly Matrix4x4 _localToWorld;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="material"></param>
        /// <param name="localToWorld"></param>
        public LifeGameRendererCombinedMesh(Mesh mesh, Material material, Matrix4x4 localToWorld)
        {
            // 描画に使うマテリアル
            _material = material;
            // 描画対象のセル情報 (未使用)
            //_renderBuffers = new NativeList<uint>(Allocator.Persistent);
            // インダイレクト描画用の引数バッファ (未使用)
            //_argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, _indirectArgs.Length, sizeof(uint) * _indirectArgs.Length);
            // Graphics.RenderMesh()用の描画設定
            _renderParams = new RenderParams(material);
            _renderParams.shadowCastingMode = ShadowCastingMode.On;
            _renderParams.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            _localToWorld = localToWorld;

            // 頂点を準備
            var vertices = new NativeArray<float3>(mesh.vertices.Length, Allocator.Persistent);
            for (var i = 0; i < mesh.vertices.Length; i++)
            {
                vertices[i] = mesh.vertices[i] + new Vector3(0.5f, 0.5f, 0.5f);
            }

            // 面を準備
            var triangles = new NativeArray<int>(mesh.triangles.Length, Allocator.Persistent);
            for (var i = 0; i < mesh.triangles.Length; i++)
            {
                triangles[i] = mesh.triangles[i];
            }

            // テクスチャ座標を準備
            var uv = new NativeArray<float2>(mesh.uv.Length, Allocator.Persistent);
            for (var i = 0; i < mesh.uv.Length; i++)
            {
                uv[i] = mesh.uv[i];
            }

            // 法線テクスチャ座標を準備
            var normals = new NativeArray<float3>(mesh.uv.Length, Allocator.Persistent);
            for (var i = 0; i < mesh.normals.Length; i++)
            {
                normals[i] = mesh.normals[i];
            }

            // 元となるメッシュの頂点・面・テクスチャ座標・法線テクスチャ座標をNativeArrayに変換して構造体に保持
            _lifeGameMesh = new LifeGameUtil.LifeGameMesh()
            {
                vertices = vertices,
                triangles = triangles,
                uv = uv,
                normals = normals
            };
        }

        /// <summary>
        /// 各ステップの状態を1つのメッシュとして蓄積
        /// </summary>
        /// <param name="input"></param>
        /// <param name="height"></param>
        public void AddRenderBuffer(DataChunk input, int height)
        {
            var mesh = LifeGameUtil.CreateMesh(ref input, ref _lifeGameMesh, height);
            _meshes.Add(mesh);
        }

        /// <summary>
        /// _meshesに蓄積されたすべてのメッシュをGraphics.RenderMesh()で描画
        /// </summary>
        /// <param name="bounds"></param>
        public void RenderInstance(Bounds bounds)
        {
            foreach (var mesh in _meshes)
            {
                // Graphics.DrawMesh(mesh, _localToWorld * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one), _material,
                //     LayerMask.NameToLayer("Default"));

                Graphics.RenderMesh(_renderParams, mesh, 0, _localToWorld * Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            _gb?.Dispose();
            _argsBuffer?.Dispose();
            _renderBuffers.Dispose();
            _lifeGameMesh.vertices.Dispose();
            _lifeGameMesh.triangles.Dispose();
            _lifeGameMesh.uv.Dispose();
            _lifeGameMesh.normals.Dispose();
        }
    }
}
