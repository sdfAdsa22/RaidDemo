using UnityEngine;
using UnityEngine.UI;

namespace RaidDemo.UI
{
    /// <summary>
    /// 角色选择界面的实时 3D 预览部分。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么把预览台搬到 (10000,10000,10000)：</b>主相机的远裁剪面只有 300 米，
    /// 预览台在这个距离外，因此不需要给角色单独开 Layer，也不会被主画面看到。
    /// 预览相机只看预览台附近 20 米，价格便宜且不会干扰正常渲染。</para>
    /// <para>RenderTexture 必须显式 Release 并清空 <c>camera.targetTexture</c>，
    /// 否则退出播放模式时会报 “Releasing render texture that is set as Camera.targetTexture”。</para>
    /// </remarks>
    public sealed partial class CharacterSelectScreen
    {
        private const float PreviewPanelWidth = 560f;
        private const float PreviewPanelHeight = 560f;
        private const float PreviewDistance = 4.2f;

        private RawImage m_PreviewImage;
        private RenderTexture m_PreviewTexture;
        private Camera m_PreviewCamera;
        private Transform m_PreviewStage;
        private GameObject m_PreviewModel;
        private Material m_PreviewFloorMaterial;

        /// <summary>构建右侧预览框与预览相机。</summary>
        private void BuildPreview(RectTransform panel)
        {
            var previewPanel = UiFactory.CreatePanel(
                panel,
                "PreviewPanel",
                new Vector2(PreviewPanelWidth, PreviewPanelHeight),
                UiSprites.CardDim,
                new Vector2(PanelSize.x - PreviewPanelWidth - Padding, TitleBarHeight + Padding));

            var imageHost = new GameObject("PreviewImage", typeof(RectTransform), typeof(RawImage));
            var imageRect = (RectTransform)imageHost.transform;
            imageRect.SetParent(previewPanel, worldPositionStays: false);
            UiFactory.Stretch(imageRect);
            imageRect.offsetMin = new Vector2(12f, 12f);
            imageRect.offsetMax = new Vector2(-12f, -12f);

            m_PreviewImage = imageHost.GetComponent<RawImage>();
            m_PreviewImage.raycastTarget = false;

            m_PreviewTexture = new RenderTexture(560, 560, 24, RenderTextureFormat.ARGB32)
            {
                name = "CharacterPreview",
                antiAliasing = 2,
                useMipMap = false,
            };
            m_PreviewTexture.Create();
            m_PreviewImage.texture = m_PreviewTexture;

            BuildPreviewStage();
        }

        /// <summary>创建预览台、相机与地面。</summary>
        private void BuildPreviewStage()
        {
            var stageHost = new GameObject("CharacterPreviewStage");
            m_PreviewStage = stageHost.transform;
            m_PreviewStage.position = new Vector3(10000f, 10000f, 10000f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(m_PreviewStage, worldPositionStays: false);
            floor.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            floor.transform.localScale = new Vector3(2.6f, 0.12f, 2.6f);
            var collider = floor.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            m_PreviewFloorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "M_CharacterPreviewFloor"
            };
            m_PreviewFloorMaterial.SetColor("_BaseColor", UiPalette.PaperDim);
            m_PreviewFloorMaterial.SetFloat("_Smoothness", 0.05f);
            m_PreviewFloorMaterial.SetFloat("_Metallic", 0f);
            floor.GetComponent<Renderer>().sharedMaterial = m_PreviewFloorMaterial;

            var cameraHost = new GameObject("PreviewCamera");
            cameraHost.transform.SetParent(m_PreviewStage, worldPositionStays: false);
            m_PreviewCamera = cameraHost.AddComponent<Camera>();
            m_PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            m_PreviewCamera.backgroundColor = UiPalette.MenuBackdropDeep;
            m_PreviewCamera.cullingMask = ~0;
            m_PreviewCamera.fieldOfView = 34f;
            m_PreviewCamera.nearClipPlane = 0.1f;
            m_PreviewCamera.farClipPlane = 20f;
            m_PreviewCamera.allowHDR = false;
            m_PreviewCamera.targetTexture = m_PreviewTexture;

            // 相机看向角色胸口高度；预览台是它的父节点，因此相机跟随舞台，
            // 不会被玩家在主场景里的移动影响。
            cameraHost.transform.localPosition = new Vector3(0f, 1.2f, -PreviewDistance);
            cameraHost.transform.LookAt(
                m_PreviewStage.position + (Vector3.up * 0.85f),
                Vector3.up);
        }

        /// <summary>按当前选中项重建预览模型。</summary>
        private void RebuildPreviewModel()
        {
            if (m_PreviewStage == null)
            {
                return;
            }

            if (m_PreviewModel != null)
            {
                Destroy(m_PreviewModel);
                m_PreviewModel = null;
            }

            var option = FindOption(m_SelectedId);
            if (option == null || option.Prefab == null)
            {
                return;
            }

            m_PreviewModel = Instantiate(option.Prefab, m_PreviewStage, false);
            m_PreviewModel.name = "Preview_" + option.Id;
            m_PreviewModel.transform.localPosition = Vector3.zero;
            m_PreviewModel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            m_PreviewModel.transform.localScale = Vector3.one;

            var animator = m_PreviewModel.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.SetBool("Armed", false);
            }
        }

        /// <summary>缓慢自转，让玩家能看到角色的侧面与背面。</summary>
        private void RotatePreviewModel()
        {
            if (m_PreviewModel != null)
            {
                m_PreviewModel.transform.Rotate(0f, 22f * Time.unscaledDeltaTime, 0f, Space.World);
            }
        }

        private void SetPreviewCameraEnabled(bool enabled)
        {
            if (m_PreviewCamera != null)
            {
                m_PreviewCamera.enabled = enabled;
            }
        }

        /// <summary>释放预览相机与 RenderTexture。</summary>
        private void CleanupPreview()
        {
            if (m_PreviewCamera != null)
            {
                m_PreviewCamera.targetTexture = null;
                Destroy(m_PreviewCamera.gameObject);
                m_PreviewCamera = null;
            }

            if (m_PreviewTexture != null)
            {
                if (RenderTexture.active == m_PreviewTexture)
                {
                    RenderTexture.active = null;
                }

                m_PreviewTexture.Release();
                Destroy(m_PreviewTexture);
                m_PreviewTexture = null;
            }

            if (m_PreviewModel != null)
            {
                Destroy(m_PreviewModel);
                m_PreviewModel = null;
            }

            if (m_PreviewStage != null)
            {
                Destroy(m_PreviewStage.gameObject);
                m_PreviewStage = null;
            }

            if (m_PreviewFloorMaterial != null)
            {
                Destroy(m_PreviewFloorMaterial);
                m_PreviewFloorMaterial = null;
            }

            m_PreviewImage = null;
        }
    }
}
