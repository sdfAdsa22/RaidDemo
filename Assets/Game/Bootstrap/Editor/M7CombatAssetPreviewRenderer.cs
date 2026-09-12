using System.Text;
using RaidDemo.Presentation;
using UnityEditor;
using UnityEngine;

namespace RaidDemo.Bootstrap.Editor
{
    /// <summary>
    /// 渲染武器模型与战斗特效的预览图，供负责人验收观感。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不直接进游戏截图：</b>进游戏需要有人操作鼠标、先装备武器、再对准角度，
    /// 而"这把枪看起来对不对"是一个纯静态的问题。离屏渲染可以在无人值守的情况下反复出图，
    /// 换素材前后各出一张就能直接对比。</para>
    /// <para>预览对象统一放到用户层 30 并只让预览相机渲染该层：编辑器当前打开的场景里
    /// 可能有整张地图，不隔离会把地图也拍进来（同 M7-P-04）。</para>
    /// <para>输出到 <c>Library/M7Preview/</c>——Library 不入库，看完即可删。</para>
    /// </remarks>
    public static class M7CombatAssetPreviewRenderer
    {
        /// <summary>输出目录。</summary>
        private const string OutputFolder = "Library/M7Preview";

        /// <summary>预览专用层。</summary>
        private const int PreviewLayer = 30;

        /// <summary>预览场景的原点：远离地图，避免与场景物体重叠。</summary>
        private static readonly Vector3 PreviewOrigin = new Vector3(2000f, 2000f, 2000f);

        private const int ImageWidth = 1280;

        private const int ImageHeight = 720;

        /// <summary>菜单入口。</summary>
        [MenuItem("RaidDemo/M7/渲染武器与特效预览图")]
        public static void RenderFromMenu()
        {
            Debug.Log(RenderAll());
        }

        /// <summary>渲染全部预览图。</summary>
        /// <returns>输出目录与文件列表。</returns>
        public static string RenderAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PresentationCatalog>(
                M7PresentationCatalogBuilder.CatalogPath);
            if (catalog == null)
            {
                return $"[RaidDemo] 找不到表现层目录 {M7PresentationCatalogBuilder.CatalogPath}，请先执行重建菜单。";
            }

            var folder = System.IO.Path.GetFullPath(OutputFolder);
            System.IO.Directory.CreateDirectory(folder);

            var root = new GameObject("M7CombatPreviewRoot");
            root.transform.position = PreviewOrigin;
            SetLayerRecursively(root.transform);

            var light = CreateLight(root.transform);
            var character = CreateCharacter(root.transform);
            var weaponView = CreateWeaponView(root.transform, character, catalog);
            var summary = new StringBuilder("[RaidDemo] 战斗资产预览图已输出：");

            RenderWeapon(summary, weaponView, character, 3, "01_步枪_游戏视角");
            RenderWeapon(summary, weaponView, character, 2, "02_手枪_游戏视角");
            RenderWeapon(summary, weaponView, character, 3, "03_步枪_特写",
                pitch: 14f, distance: 2.6f, focusOffset: new Vector3(0.5f, 1.0f, 0f));
            RenderWeapon(summary, weaponView, character, 2, "05_手枪_特写",
                pitch: 14f, distance: 1.7f, focusOffset: new Vector3(0.25f, 1.0f, 0f));
            RenderEffects(summary, root.transform, catalog, light);

            Object.DestroyImmediate(root);
            return summary.ToString();
        }

        /// <summary>渲染一张"角色手持某把枪"的图。</summary>
        private static void RenderWeapon(
            StringBuilder summary,
            PlayerWeaponView view,
            GameObject character,
            int gridWidth,
            string fileName,
            float pitch = 62f,
            float distance = 13f,
            Vector3 focusOffset = default)
        {
            if (view == null || character == null)
            {
                return;
            }

            // 更新视图：模型按格宽自动在长枪/短枪之间切换，预览因此与游戏里用的是同一套判定。
            view.UpdateView(character.transform.position, 0f, true, gridWidth);
            if (!view.UsesRealModel)
            {
                summary.Append('\n').Append("  · ").Append(fileName).Append("（没有武器模型，正在用灰盒兜底）");
            }

            var focus = character.transform.position +
                        (focusOffset == default ? new Vector3(0f, 1.0f, 0f) : focusOffset);
            if (RenderShot(focus, pitch, 0f, distance, 55f, fileName))
            {
                summary.Append('\n').Append("  ").Append(OutputFolder).Append('/').Append(fileName).Append(".png");
            }
        }

        /// <summary>渲染一张特效集合图。</summary>
        private static void RenderEffects(
            StringBuilder summary,
            Transform root,
            PresentationCatalog catalog,
            GameObject light)
        {
            var instances = new[]
            {
                SpawnEffect(catalog.MuzzleFlashPrefab, root, new Vector3(-1.6f, 1.5f, -0.4f), 0.05f),
                SpawnEffect(catalog.ImpactSparkPrefab, root, new Vector3(-0.4f, 1.3f, -0.4f), 0.08f),
                SpawnEffect(catalog.ImpactDustPrefab, root, new Vector3(-0.4f, 1.3f, -0.4f), 0.12f),
                SpawnEffect(catalog.ImpactFleshPrefab, root, new Vector3(0.9f, 1.3f, -0.4f), 0.10f)
            };

            // 特效要有人体尺度参照才看得出大小。参照角色放在特效**后面**（z 更大 = 离相机更远），
            // 否则它会把自己前面的火花与尘土挡住，预览图看起来像"这两个特效没做出来"。
            var reference = CreateCharacter(root);
            reference.transform.localPosition = new Vector3(2.6f, 0f, 0.5f);

            var focus = root.position + new Vector3(0.3f, 1.3f, -0.4f);
            var ok = RenderShot(focus, 16f, 0f, 7.5f, 48f, "04_战斗特效_枪口火焰与命中");
            if (ok)
            {
                summary.Append('\n').Append("  ").Append(OutputFolder).Append("/04_战斗特效_枪口火焰与命中.png");
            }

            foreach (var instance in instances)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }

            if (reference != null)
            {
                Object.DestroyImmediate(reference);
            }
        }

        /// <summary>实例化一个特效预制体并推进到指定时刻。</summary>
        private static GameObject SpawnEffect(GameObject prefab, Transform parent, Vector3 offset, float simulateSeconds)
        {
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = offset;
            instance.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(instance.transform);

            // 编辑模式里粒子系统不会自己推进：必须手动 Simulate 才能看到"喷到一半"的样子，
            // 否则所有特效都会以第 0 帧的空白状态被渲染出来。
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>())
            {
                system.Play(true);
                system.Simulate(simulateSeconds, true, true);
            }

            return instance;
        }

        /// <summary>创建玩家角色实例（作为比例参照）。</summary>
        private static GameObject CreateCharacter(Transform parent)
        {
            const string path = "Assets/Game/Content/Art/Characters/Player/PlayerCharacter.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(instance.transform);
            PoseArmedIfPossible(instance);
            return instance;
        }

        /// <summary>
        /// 尽量把角色摆成持枪姿态。
        /// </summary>
        /// <remarks>
        /// <para>预览在编辑模式下进行，Animator 默认不推进，角色会保持 T 字pose——
        /// 双臂水平张开 1.7 米，正好把胸前的枪挡得严严实实，看起来像"武器没做出来"。</para>
        /// <para>这里手动推进一次 Animator 并置上持枪参数。若宿主环境不支持在编辑模式求值
        /// （不同 Unity 版本行为略有差异），角色会保持 T 字pose，但枪本身仍然会被渲染出来，
        /// 因此这一步失败不会让预览失去意义。</para>
        /// </remarks>
        private static void PoseArmedIfPossible(GameObject character)
        {
            var animator = character.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetBool("Armed", true);
            animator.Update(0.2f);
        }

        /// <summary>创建武器视图并挂上两把枪的模型。</summary>
        private static PlayerWeaponView CreateWeaponView(
            Transform parent,
            GameObject character,
            PresentationCatalog catalog)
        {
            if (character == null)
            {
                return null;
            }

            var host = new GameObject("PreviewWeaponView");
            host.transform.SetParent(parent, worldPositionStays: false);
            SetLayerRecursively(host.transform);

            var view = host.AddComponent<PlayerWeaponView>();
            view.Build(
                character.transform,
                catalog.RifleWeaponPrefab,
                catalog.PistolWeaponPrefab);

            // 武器模型是 Build 之后才创建出来的，必须在这之后再刷一次层：
            // 预览相机只渲染第 30 层，漏掉这一步的症状是"两张预览图一模一样"——
            // 因为枪根本没被渲染，画面里只有角色。
            SetLayerRecursively(parent);
            return view;
        }

        /// <summary>预览用平行光。</summary>
        private static GameObject CreateLight(Transform parent)
        {
            var host = new GameObject("PreviewLight");
            host.transform.SetParent(parent, worldPositionStays: false);
            var light = host.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.97f, 0.9f);
            host.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            return host;
        }

        /// <summary>按固定机位渲染一张图。</summary>
        private static bool RenderShot(
            Vector3 focus,
            float pitch,
            float yaw,
            float distance,
            float fieldOfView,
            string fileName)
        {
            var host = new GameObject("M7CombatPreviewCamera");
            var camera = host.AddComponent<Camera>();
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f);

            // 只渲染预览层：编辑器当前场景里可能开着整张地图，不隔离会把它一起拍进来。
            camera.cullingMask = 1 << PreviewLayer;

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var forward = rotation * Vector3.forward;
            host.transform.position = focus - (forward * distance);
            host.transform.rotation = rotation;

            var renderTexture = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, ImageWidth, ImageHeight), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            System.IO.File.WriteAllBytes(
                System.IO.Path.GetFullPath($"{OutputFolder}/{fileName}.png"),
                texture.EncodeToPNG());

            // 必须先解除相机的渲染目标再销毁它：直接销毁会留下一条
            // "Releasing render texture that is set as Camera.targetTexture!" 报错，
            // 而报错会让控制台看起来像出了故障，掩盖真正的问题。
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(host);
            return true;
        }

        /// <summary>把整棵子树设为预览层。</summary>
        private static void SetLayerRecursively(Transform root)
        {
            root.gameObject.layer = PreviewLayer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child);
            }
        }
    }
}
