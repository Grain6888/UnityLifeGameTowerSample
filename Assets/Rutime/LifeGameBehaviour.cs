using LifeGame3D.Job;
using System;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LifeGame3D
{
    public enum RenderType
    {
        /// <summary>
        /// GameObjectにMeshRendererをアタッチして描画するメソッド
        /// </summary>
        GameObject,
        /// <summary>
        /// GameObjectを作らずにスクリプトのみでメッシュを描画するメソッド
        /// </summary>
        DrawMesh,
        /// <summary>
        /// GPUインスタンスを使って同一のメッシュを大量に描画するためのメソッド
        /// </summary>
        DrawMeshInstanced,
        /// <summary>
        /// DrawMeshInstancedの座表計算やライティング計算を独自に実装するメソッド
        /// </summary>
        DrawMeshInstancedProcedural,
        /// <summary>
        /// 複数のメッシュを1つのメッシュに統合するメソッド
        /// </summary>
        CombinedMesh,
    }

    public class LifeGameBehaviour : MonoBehaviour
    {
        public static event Action OnPlay;
        public static event Action OnStopped;

        /// <summary>
        /// ライフゲームの生成範囲指定 (値が大きいと処理が重くなる)
        /// </summary>
        public int3 size = new int3(64, 64, 128);

        /// <summary>
        /// Cubeの配置法を選択 (初期設定のCombinedMeshが最も高速)
        /// </summary>
        public RenderType renderType;
        private ILifeGameRenderer _renderer;

        /// <summary>
        /// 3Dデータを保持するスペース
        /// </summary>
        private DataChunk _dataChunk;

        private int _currentHeight;

        /// <summary>
        /// 初期配置をランダム配置するか (チェックを外すとInitialDataが使われる)
        /// </summary>
        public bool useRandom;

        /// <summary>
        /// ランダム初期化のシード値
        /// </summary>
        [SerializeField] private int seed = 372198379;

        /// <summary>
        /// タワーの生成を開始するフレーム
        /// </summary>
        [SerializeField] private float _warmUpFrame = 30;

        /// <summary>
        /// タワーを構成するメッシュ
        /// </summary>
        [SerializeField] private Mesh _mesh;

        /// <summary>
        /// タワーを構成するマテリアル
        /// </summary>
        [SerializeField] private Material _material;
        [SerializeField] private Material _materialProcedural;

        /// <summary>
        /// 初期配置のEditor拡張用のWidth
        /// </summary>
        public int initialWidth;

        /// <summary>
        /// Use Randomのチェックをつけていない場合，ここで指定した配置が初期配置になる
        /// </summary>
        public bool[] initialData;

        /// <summary>
        /// 初期化処理
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private void Awake()
        {
            // DataChunkを生成し，3Dデータを保持
            _dataChunk = new DataChunk(size.x, size.y, size.z);

            // オブジェクトのローカル座標系をワールド座標系に変換する
            var localToWorldMatrix = transform.localToWorldMatrix;

            // renderTypeに応じて適切なCubeの配置法を選択
            switch (renderType)
            {
                case RenderType.GameObject:
                    _renderer = new LifeGameRendererGameObject(_mesh, _material, localToWorldMatrix);
                    break;
                case RenderType.DrawMesh:
                    _renderer = new LifeGameRendererDrawMesh(_mesh, _material, localToWorldMatrix);
                    break;
                case RenderType.DrawMeshInstanced:
                    _renderer = new LifeGameRendererDrawMeshInstanced(_mesh, _material, localToWorldMatrix);
                    break;
                case RenderType.DrawMeshInstancedProcedural:
                    _renderer = new LifeGameRendererInstancedProcedural(_mesh, _materialProcedural,
                        new Vector3Int(size.x, size.y, size.z), localToWorldMatrix);
                    break;
                case RenderType.CombinedMesh:
                    _renderer = new LifeGameRendererCombinedMesh(_mesh, _material, localToWorldMatrix);
                    break;
                // 速度出せなかったので除外
                // case RenderType.CombinedMeshBRG:
                //     _renderer = new LifeGameRendererBatchRendererGroup(_mesh, _material);
                //     break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //初期状態をランダムまたはinitialDataに基づいて設定
            Random.InitState(seed);

            // _dataChunkの中から，y=0の層XY平面を取得
            var us = _dataChunk.Get2DimArray(0);

            if (useRandom)
            {
                for (int i = 0; i < (size.x * size.z); i++)
                {
                    if (Random.Range(0f, 1f) < 0.5f)
                    {
                        // セルに生存フラグを立てる
                        us.AddFlag(i, LifeGameFlags.Alive);
                    }
                }
            }
            else
            {
                // 中心に寄せるように
                var offsetX = size.x / 2;
                var offsetZ = size.z / 2;
                for (int z = 0; z < initialWidth; z++)
                {
                    for (int x = 0; x < initialWidth; x++)
                    {
                        if (initialData[x + z * initialWidth])
                        {
                            us.AddFlag(x + offsetX, 0, z + offsetZ, LifeGameFlags.Alive);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 生成済みの層のカウンタ
        /// </summary>
        private int _height;

        /// <summary>
        /// 初回のUpdateのフラグ
        /// </summary>
        private bool _isFirstUpdate;

        /// <summary>
        /// 更新処理
        /// </summary>
        private void Update()
        {
            // 開始フレームまで待機
            if (Time.frameCount < _warmUpFrame) return;

            // 初回UpdateでOnPlayを発火
            if (_isFirstUpdate == false)
            {
                _isFirstUpdate = true;
                // もしOnPlayに何かしらのリスナーが登録されていれば，それを呼び出す
                OnPlay?.Invoke();
            }

            // 初回Updateで0層目を生成
            if (_height == 0)
            {
                _renderer.AddRenderBuffer(_dataChunk.Get2DimArray(0), 0);
                _height++;
            }
            // 2回目以降Updateで1層目以降を生成
            else if (_height < _dataChunk.yLength)
            {
                // 直前のステップのセルの状態
                var prev = _dataChunk.Get2DimArray(_height - 1);
                // 現在のステップのセルの状態
                var current = _dataChunk.Get2DimArray(_height);

                // セルの状態を更新するジョブ
                LifeGameJob calculateJob = new LifeGameJob()
                {
                    input = prev,
                    output = current,
                };

                // ジョブをスケジューリングして，sizeのXxY個のセルを並列に処理
                var calcHandle = calculateJob.Schedule(size.x * size.z, 1);
                // ジョブの完了を待機
                calcHandle.Complete();

                // 現在のステップのセルの状態を，選択した配置法の描画バッファに追加
                _renderer.AddRenderBuffer(current, _height);
                // 層を一つ上げる
                _height++;
            }
            // sizeの範囲よりも高く積み上げたら終了
            else if (_height >= _dataChunk.yLength)
            {
                // もしOnStoppedに何かしらのリスナーが登録されていれば，それを呼び出す
                OnStopped?.Invoke();
            }

            // GPUに描画範囲を伝える
            // 生成範囲
            var vecSize = transform.localToWorldMatrix.MultiplyPoint(new Vector3(size.x, size.y, size.z));
            // Unityの3D空間の範囲
            var bounds = new Bounds();
            // Unityの3D空間の範囲を，(0,0,0)から(size.x,size.y,size.z)に指定
            bounds.SetMinMax(Vector3.zero, vecSize);
            // 生成範囲の中心点からsize分の範囲を指定して，GPUに描画範囲を伝える
            _renderer.RenderInstance(new Bounds(vecSize * 0.5f, vecSize));
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        private void OnDestroy()
        {
            _renderer.Dispose();
            _dataChunk.Dispose();
        }
    }
}
