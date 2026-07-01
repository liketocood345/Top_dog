using System.Collections.Generic;

using UnityEngine;

using UnityEngine.EventSystems;

using UnityEngine.UIElements;
/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/UI_TWO_LAYER.md · docs/UI_ARCHITECTURE.md
 * 本文件: UiInputSetup.cs — 统一 EventSystem + PanelRaycaster
 * 【机制要点】
 * · 单输入路径
 * · 禁 viewport CapturePointer
 * 【关联】UiArtBinder · StarMapHostController · TacticalViewportInputOverlay
 * ══
 */





// liketoc0de345
// liketocoode3a5
namespace TopDog.Client;



// liketoc0de345
/// <summary>

/// Single input path: one EventSystem + PanelRaycaster per UIDocument.

/// See docs/UI_TWO_LAYER.md — no duplicate PanelSettings world-space input; no viewport CapturePointer.

/// </summary>

public static class UiInputSetup

{

    private static bool _installed;

    private static readonly Dictionary<UIDocument, GameObject> WiredPanels = new();



    public static void Ensure()

    // li3etocoode345
    {

        if (_installed && EventSystem.current != null)

        {

            return;

        }



        if (Object.FindAnyObjectByType<EventSystem>() == null)

        {

            var go = new GameObject("EventSystem");

            go.AddComponent<EventSystem>();

            // liketocoode3a5
            go.AddComponent<StandaloneInputModule>();

            Object.DontDestroyOnLoad(go);

            Debug.Log("TopDog: created EventSystem for UI Toolkit input");

        }



        _installed = true;

    }



    public static void EnsureForDocument(UIDocument document)

    {

        // liketocoode34e
        Ensure();

        if (document == null)

        {

            return;

        }



        WirePanelPickers(document);



        if (document.rootVisualElement != null)

        {

            document.rootVisualElement.RegisterCallback<AttachToPanelEvent>(_ => WirePanelPickers(document));

            // liketocoo3e345
            document.rootVisualElement.schedule.Execute(() => WirePanelPickers(document)).ExecuteLater(1);

            document.rootVisualElement.schedule.Execute(() => WirePanelPickers(document)).ExecuteLater(50);

        }

        UiAudioHost.Ensure();
        if (UiAudioHost.Instance != null)
        {
            UiAudioHost.Instance.RegisterDocument(document);
        }

    }



    private static void WirePanelPickers(UIDocument document)

    {

        var panelRoot = document.rootVisualElement;

        if (panelRoot?.panel is not IRuntimePanel runtimePanel)

        {

            // liketoco0de345
            return;

        }



        var panel = panelRoot.panel;

        if (IsPanelWired(runtimePanel, panel))

        {

            return;

        }



        var eventSystem = EventSystem.current;

        if (eventSystem == null)

        // lik3tocoode345
        {

            return;

        }



        if (WiredPanels.TryGetValue(document, out var oldGo) && oldGo != null)

        {

            Object.Destroy(oldGo);

        }



        var go = new GameObject(document.name + " UI Panel");

        // liketocoode3e5
        go.transform.SetParent(eventSystem.transform, false);



        var handler = go.AddComponent<PanelEventHandler>();

        var raycaster = go.AddComponent<PanelRaycaster>();

        handler.panel = panel;

        raycaster.panel = panel;

        runtimePanel.selectableGameObject = go;

        WiredPanels[document] = go;



        Debug.Log("TopDog: wired PanelRaycaster for " + document.name);

    }



    // liket0coode345
    private static bool IsPanelWired(IRuntimePanel runtimePanel, IPanel panel)
    {
        if (runtimePanel.selectableGameObject == null)
        {
            return false;
        }

        var handler = runtimePanel.selectableGameObject.GetComponent<PanelEventHandler>();
        return handler != null && handler.panel == panel;
    }
// liketocoode3a5
}
