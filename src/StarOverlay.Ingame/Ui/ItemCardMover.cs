using System;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The game opens the card of a clicked chat link where its own chat bubble is (a hidden one, far from where the user pressed). After such a
/// click the card is moved to the cursor: its top left corner goes just under and right of the point that was pressed, kept inside the screen.
/// </summary>
static class ItemCardMover
{
    const string CardPath = "all_item_info_tips(Clone)/tipsPartent/tips_item_info_popup_pc(Clone)";
    static float _until;
    static Vector2 _cursor;
    static Transform _layer;
    static float _nextLayerSearch;

    /// <summary>A link of the game was clicked for a press at this screen point: its card is moved there once it shows.</summary>
    internal static void Request(Vector2 cursor)
    {
        _cursor = cursor;
        _until = Time.realtimeSinceStartup + 2f;
    }

    public static void Poll()
    {
        if (_until == 0f) return;
        if (Time.realtimeSinceStartup > _until)
        {
            _until = 0f;
            return;
        }

        try
        {
            var card = FindCard();
            if (card == null || !card.gameObject.activeInHierarchy) return;
            var rect = card.GetComponent<RectTransform>();
            var canvas = card.GetComponentInParent<Canvas>();
            var root = canvas == null ? null : canvas.rootCanvas;
            if (rect == null || root == null) return;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var box = rect.rect;
            var topLeft = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(box.xMin, box.yMax, 0f)));
            var bottomRight = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(box.xMax, box.yMin, 0f)));
            var width = bottomRight.x - topLeft.x;
            var height = topLeft.y - bottomRight.y;
            if (width < 8f || height < 8f) return; // not laid out yet

            var x = Math.Clamp(_cursor.x + 12f, 0f, Math.Max(0f, Screen.width - width));
            var top = Math.Clamp(_cursor.y - 12f, Math.Min(height, Screen.height), Screen.height);
            var dx = x - topLeft.x;
            var dy = top - topLeft.y;
            if (Math.Abs(dx) < 1.5f && Math.Abs(dy) < 1.5f) return;
            var canvasRect = root.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, topLeft, camera, out var from);
            RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, new Vector2(topLeft.x + dx, topLeft.y + dy), camera, out var to);
            rect.position += to - from;
        }
        catch (Exception e)
        {
            _until = 0f;
            Plugin.L.LogWarning("item card position: " + e.Message);
        }
    }

    static Transform FindCard()
    {
        if (_layer == null)
        {
            if (Time.realtimeSinceStartup < _nextLayerSearch) return null;
            _nextLayerSearch = Time.realtimeSinceStartup + 0.25f;
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isRootCanvas || canvas.name != "zuiroot") continue;
                _layer = canvas.transform.Find("UILayerTipTop");
                break;
            }

            if (_layer == null) return null;
        }

        return _layer.Find(CardPath);
    }
}
