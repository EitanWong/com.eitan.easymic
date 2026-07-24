#if UNITY_EDITOR

using System;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CreateAssetMenu(menuName = "EasyMic/Samples/AI Chat README", fileName = "AIChatSampleReadme")]
    public sealed class AIChatSampleReadme : ScriptableObject
    {
        [Serializable]
        public sealed class Document
        {
            [SerializeField] private string _title = string.Empty;
            [SerializeField, TextArea(2, 5)] private string _summary = string.Empty;
            [SerializeField] private Section[] _sections = Array.Empty<Section>();

            public string Title => _title;
            public string Summary => _summary;
            public Section[] Sections => _sections ?? Array.Empty<Section>();
        }

        [Serializable]
        public sealed class Section
        {
            [SerializeField] private string _heading = string.Empty;
            [SerializeField, TextArea(2, 8)] private string _body = string.Empty;
            [SerializeField] private string _linkLabel = string.Empty;
            [SerializeField] private string _url = string.Empty;

            public string Heading => _heading;
            public string Body => _body;
            public string LinkLabel => _linkLabel;
            public string Url => _url;
        }

        [SerializeField] private Document _english = new Document();
        [SerializeField] private Document _chineseSimplified = new Document();

        public Document GetDocument(AIChatSetupLanguage language)
        {
            return language == AIChatSetupLanguage.ChineseSimplified
                ? _chineseSimplified
                : _english;
        }
    }
}

#endif
