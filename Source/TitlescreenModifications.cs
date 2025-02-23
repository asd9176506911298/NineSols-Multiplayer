using BepInEx.Bootstrap;
using I2.Loc;
using NineSolsAPI.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NineSolsAPI.Menu;

internal class TitlescreenModifications {
    private UIControlGroup? group;
    private UIControlButton? button;

    private const bool Enable = true;

    public void Load() {
        MaybeExtendMainMenu(SceneManager.GetActiveScene());
    }

    public void Unload() {
        if (button != null) Object.Destroy(button.gameObject);
        if (group != null) Object.Destroy(group.gameObject);
    }


    public void MaybeExtendMainMenu(Scene scene) {
        if (!Enable) return;

        if (scene.name != "TitleScreenMenu") return;
        if (button) return;

        group = CreateUiControlGroup();
        button = CreateOptionsButton();
        button.clickToShowGroup = group;
    }


    private UIControlButton CreateOptionsButton() {
        var optionsButton = GameObject.Find("MainMenuButton_Option");

        var modOptions = ObjectUtils.InstantiateAutoReference(optionsButton, optionsButton.transform.parent, false);
        Object.Destroy(modOptions.GetComponentInChildren<Localize>());
        modOptions.GetComponentInChildren<TMP_Text>().text = "MultPlayer";
        modOptions.gameObject.transform.SetSiblingIndex(optionsButton.transform.GetSiblingIndex() + 1);
        var modOptionButton = modOptions.GetComponentInChildren<UIControlButton>();

        return modOptionButton;
    }

    private UIControlGroup CreateUiControlGroup() {
        var providers = StartMenuLogic.Instance.gameObject.GetComponentInChildren<UICursorProvider>();

        var obj = new GameObject("ModOptions Panel");
        obj.transform.SetParent(providers.transform, false);

        var rectTransform = obj.AddComponent<RectTransform>();
        var uiControlGroup = obj.AddComponent<UIControlGroup>();
        var rcgUiPanel = obj.GetComponent<RCGUIPanel>();
        obj.AddComponent<CanvasRenderer>();
        obj.AddComponent<SelectableNavigationProvider>();
        obj.AddComponent<Animator>();
        AutoAttributeManager.AutoReference(obj);

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;

        rcgUiPanel.OnShowInit = new UnityEvent();
        rcgUiPanel.OnHideInit = new UnityEvent();
        rcgUiPanel.OnShowComplete = new UnityEvent();
        rcgUiPanel.OnHideComplete = new UnityEvent();

        var layout = new GameObject();
        var layoutTransform = layout.AddComponent<RectTransform>();
        layoutTransform.anchorMin = new Vector2(0.5f, 0.5f);
        layoutTransform.anchorMax = new Vector2(0.5f, 0.5f);
        layoutTransform.sizeDelta = new Vector2(600, 800);
        layout.AddComponent<VerticalLayoutGroup>().spacing = 20;
        layout.transform.SetParent(uiControlGroup.transform, false);

        var title = new GameObject();
        title.AddComponent<TextMeshProUGUI>().text = "Mod options";
        title.AddComponent<LayoutElement>();
        title.transform.SetParent(layout.transform, false);

        var padding = new GameObject();
        padding.AddComponent<CanvasRenderer>();
        padding.AddComponent<RectTransform>();
        padding.AddComponent<LayoutElement>().minHeight = 0;
        padding.transform.SetParent(layout.transform, false);

        var buttonOrig = ObjectUtils.FindDisabledByName<Button>("Show HUD")!;

        foreach (var plugin in Chainloader.PluginInfos) {
            var c = ObjectUtils.InstantiateAutoReference(buttonOrig.gameObject, layout.transform);
            c.GetComponentInChildren<TMP_Text>().text = $"{plugin.Key} {plugin.Value.Metadata.Version}";
            var flag = new FlagFieldEntryInt();
            var intFlag = ScriptableObject.CreateInstance<GameFlagInt>();
            intFlag.field = new FlagFieldInt();
            flag.flagBase = intFlag;
            flag.fieldName = "field";
        }


        uiControlGroup.defaultSelectable = layout.GetComponentInChildren<Selectable>();

        return uiControlGroup;
    }
}