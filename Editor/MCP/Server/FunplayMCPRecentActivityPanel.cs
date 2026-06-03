// Copyright (C) Funplay. Licensed under MIT.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Funplay.Editor.MCP.Server
{
    internal sealed class FunplayMCPRecentActivityPanel : IDisposable
    {
        private readonly MCPServerService _server;
        private readonly List<Texture2D> _previewTextures = new List<Texture2D>();
        private ScrollView _scrollView;

        public FunplayMCPRecentActivityPanel(MCPServerService server)
        {
            _server = server;
        }

        public void AddTo(VisualElement parent)
        {
            ClearPreviewTextures();

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 12;
            header.style.marginBottom = 4;

            var label = new Label("Recent Activity");
            label.style.fontSize = 12;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.75f, 0.75f, 0.75f);
            label.style.flexGrow = 1;
            header.Add(label);

            var clearButton = new Button(() =>
            {
                _server.InteractionLog.Clear();
                ClearPreviewTextures();
                _scrollView?.contentContainer.Clear();
            });
            clearButton.text = "Clear";
            clearButton.style.height = 20;
            clearButton.style.width = 50;
            header.Add(clearButton);

            parent.Add(header);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            _scrollView.style.borderTopLeftRadius = 4;
            _scrollView.style.borderTopRightRadius = 4;
            _scrollView.style.borderBottomLeftRadius = 4;
            _scrollView.style.borderBottomRightRadius = 4;
            _scrollView.style.paddingLeft = 6;
            _scrollView.style.paddingRight = 6;
            _scrollView.style.paddingTop = 4;
            _scrollView.style.paddingBottom = 4;
            parent.Add(_scrollView);

            var entries = _server.InteractionLog.GetEntries();
            for (int i = entries.Count - 1; i >= 0; i--)
                AddRow(entries[i]);
        }

        public void OnEntryAdded(MCPLogEntry entry)
        {
            EditorApplication.delayCall += () =>
            {
                if (_scrollView == null)
                    return;

                AddRow(entry);
                EditorApplication.delayCall += () =>
                {
                    if (_scrollView != null)
                        _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
                };
            };
        }

        public void Dispose()
        {
            ClearPreviewTextures();
            _scrollView = null;
        }

        private void AddRow(MCPLogEntry entry)
        {
            var badgeText = GetBadgeText(entry.Status);
            var accentColor = GetAccentColor(entry.Status);

            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.19f, 0.19f, 0.19f);
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = accentColor;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 5;
            card.style.paddingBottom = 5;
            card.style.marginBottom = 3;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;

            var timeLabel = new Label(entry.Timestamp.ToString("HH:mm:ss"));
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            timeLabel.style.marginRight = 6;
            timeLabel.style.minWidth = 48;
            topRow.Add(timeLabel);

            var toolLabel = new Label(entry.ToolName);
            toolLabel.style.fontSize = 12;
            toolLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolLabel.style.color = new Color(0.88f, 0.88f, 0.88f);
            toolLabel.style.flexGrow = 1;
            topRow.Add(toolLabel);

            var badge = new Label(badgeText);
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = Color.white;
            badge.style.backgroundColor = accentColor;
            badge.style.borderTopLeftRadius = 3;
            badge.style.borderTopRightRadius = 3;
            badge.style.borderBottomLeftRadius = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.paddingLeft = 5;
            badge.style.paddingRight = 5;
            badge.style.paddingTop = 1;
            badge.style.paddingBottom = 1;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            topRow.Add(badge);

            card.Add(topRow);

            if (!string.IsNullOrEmpty(entry.ResultSummary))
            {
                var summaryLabel = new Label(entry.ResultSummary);
                summaryLabel.style.fontSize = 11;
                summaryLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                summaryLabel.style.marginTop = 3;
                summaryLabel.style.whiteSpace = WhiteSpace.Normal;
                summaryLabel.style.overflow = Overflow.Hidden;
                card.Add(summaryLabel);
            }

            if (!string.IsNullOrEmpty(entry.ImageDataUri) &&
                TryCreateImagePreview(entry.ImageDataUri, out var preview))
            {
                card.Add(preview);
            }

            _scrollView?.contentContainer.Add(card);
        }

        internal static string GetBadgeText(MCPToolCallStatus status)
        {
            switch (status)
            {
                case MCPToolCallStatus.Success:
                    return "OK";
                case MCPToolCallStatus.Interrupted:
                    return "INT";
                default:
                    return "ERR";
            }
        }

        private static Color GetAccentColor(MCPToolCallStatus status)
        {
            switch (status)
            {
                case MCPToolCallStatus.Success:
                    return new Color(0.3f, 0.75f, 0.4f);
                case MCPToolCallStatus.Interrupted:
                    return new Color(0.95f, 0.68f, 0.25f);
                default:
                    return new Color(0.9f, 0.35f, 0.35f);
            }
        }

        private bool TryCreateImagePreview(string imageDataUri, out Image preview)
        {
            preview = null;
            const string prefix = "data:image/png;base64,";
            if (string.IsNullOrEmpty(imageDataUri) || !imageDataUri.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            try
            {
                var bytes = Convert.FromBase64String(imageDataUri.Substring(prefix.Length));
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return false;
                }

                _previewTextures.Add(texture);

                preview = new Image
                {
                    image = texture,
                    scaleMode = ScaleMode.ScaleToFit
                };
                preview.style.height = 150;
                preview.style.marginTop = 6;
                preview.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                preview.style.borderTopLeftRadius = 3;
                preview.style.borderTopRightRadius = 3;
                preview.style.borderBottomLeftRadius = 3;
                preview.style.borderBottomRightRadius = 3;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ClearPreviewTextures()
        {
            foreach (var texture in _previewTextures)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }

            _previewTextures.Clear();
        }
    }
}
