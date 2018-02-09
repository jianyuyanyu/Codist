﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using AppHelpers;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace Codist.Classifiers
{
	[Export(typeof(IViewTaggerProvider))]
    [ContentType("code")]
    [TagType(typeof(ClassificationTag))]
    public class CodeTaggerProvider : IViewTaggerProvider
    {
		[Import]
		internal IClassificationTypeRegistryService ClassificationRegistry = null;

		[Import]
		internal IBufferTagAggregatorFactoryService Aggregator = null;

		ITagAggregator<IClassificationTag> _Tagger;
		ITextView _TextView;
		CodeTagger _CodeTagger;

		public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
			_Tagger = Aggregator.CreateTagAggregator<IClassificationTag>(buffer);
			_TextView = textView;
			textView.Closed += TextViewClosed;
			var tags = textView.Properties.GetOrCreateSingletonProperty(() => new TaggerResult());
			_CodeTagger = new CodeTagger(ClassificationRegistry, _Tagger, tags, CodeTagger.GetCodeType(textView.TextBuffer.ContentType));
			return _CodeTagger as ITagger<T>;
        }

		void TextViewClosed(object sender, EventArgs args) {
			_Tagger.Dispose();
			_TextView.Closed -= TextViewClosed;
			_CodeTagger.Dispose();
		}
    }

	enum CodeType
	{
		None, CSharp, Markup
	}

	sealed class CodeTagger : ITagger<ClassificationTag>, IDisposable
    {
		static ClassificationTag[] __CommentClassifications;
		//static ClassificationTag _exitClassification;
		static ClassificationTag _abstractionClassification;
		readonly ITagAggregator<IClassificationTag> _Aggregator;
		readonly TaggerResult _Tags;
		readonly CodeType _CodeType;
#if DEBUG
		readonly HashSet<string> _ClassificationTypes = new HashSet<string>();
#endif
		static readonly string[] __CSharpComments = { "//", "/*" };
		static readonly string[] __Comments = { "//", "/*", "'", "#", "<!--" };

#pragma warning disable 67
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
#pragma warning restore 67

        internal CodeTagger(IClassificationTypeRegistryService registry, ITagAggregator<IClassificationTag> aggregator, TaggerResult tags, CodeType codeType)
        {
			if (__CommentClassifications == null) {
				var t = typeof(CommentStyleTypes);
				var styleNames = Enum.GetNames(t);
				__CommentClassifications = new ClassificationTag[styleNames.Length];
				foreach (var styleName in styleNames) {
					var f = t.GetField(styleName);
					var d = f.GetCustomAttribute<ClassificationTypeAttribute>();
					if (d == null) {
						continue;
					}
					var ct = registry.GetClassificationType(d.ClassificationTypeNames);
					__CommentClassifications[(int)f.GetValue(null)] = new ClassificationTag(ct);
				}
			}
			//_exitClassification = new ClassificationTag(registry.GetClassificationType(Constants.CodeReturnKeyword));
			_abstractionClassification = new ClassificationTag(registry.GetClassificationType(Constants.CodeAbstractionKeyword));

            _Aggregator = aggregator;
			_Tags = tags;
			_CodeType = codeType;
			_Aggregator.BatchedTagsChanged += AggregateorBatchedTagsChanged;
		}

		internal FrameworkElement Margin { get; set; }

		public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) {
				yield break;
			}

			var snapshot = spans[0].Snapshot;
			var contentType = snapshot.TextBuffer.ContentType;
            if (!contentType.IsOfType("code")) {
				yield break;
			}
			IEnumerable<IMappingTagSpan<IClassificationTag>> tagSpans;
			try {
				if (_Tags.LastParsed == 0) {
					// perform a full parse for the first time
					Debug.WriteLine("Full parse");
					tagSpans = _Aggregator.GetTags(new SnapshotSpan(snapshot, 0, snapshot.Length));
					_Tags.LastParsed = snapshot.Length;
				}
				else {
					var start = spans[0].Start;
					var end = spans[spans.Count - 1].End;
					Debug.WriteLine($"Get tag [{start.Position}..{end.Position})");

					tagSpans = _Aggregator.GetTags(spans);
				}
			}
			catch (ObjectDisposedException ex) {
				// HACK: TagAggregator could be disposed during editing, to be investigated further
				Debug.WriteLine(ex.Message);
				yield break;
			}

			foreach (var tagSpan in tagSpans) {
				var className = tagSpan.Tag.ClassificationType.Classification;
#if DEBUG
				if (_ClassificationTypes.Add(className)) {
					Debug.WriteLine("Classification type: " + className);
				}
#endif
				var ss = tagSpan.Span.GetSpans(snapshot)[0];
				if (_CodeType == CodeType.CSharp) {
					switch (className) {
						case Constants.CodeClassName:
						case Constants.CodeInterfaceName:
						case Constants.CodeStructName:
						case Constants.CodeEnumName:
							if (Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.TypeDeclaration)) {
								Debug.WriteLine($"find def: {className} at {tagSpan.Span.Start.GetPoint(tagSpan.Span.AnchorBuffer, PositionAffinity.Predecessor).Value.Position}");
								yield return _Tags.Add(new TagSpan<ClassificationTag>(tagSpan.Span.GetSpans(snapshot)[0], (ClassificationTag)tagSpan.Tag));
							}
							continue;
						case Constants.CodePreprocessorKeyword:
							if (Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.CompilerDirective)) {
								if (Matches(ss, "region") || Matches(ss, "pragma") || Matches(ss, "if") || Matches(ss, "else")) {
									yield return _Tags.Add(new TagSpan<ClassificationTag>(ss, (ClassificationTag)tagSpan.Tag));
								}
							}
							continue;
						//case Constants.CodeKeyword:
						//	if (Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.Declaration)) {
						//		if (Matches(ss, "abstract") || Matches(ss, "override") || Matches(ss, "virtual")) {
						//			yield return _Tags.Add(new TagSpan<ClassificationTag>(ss, _abstractionClassification));
						//		}
						//	}
						//	continue;
						default:
							break;
					}
				}

				if (Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.SpecialComment)) {
					var c = TagComments(className, ss, tagSpan);
					if (c != null) {
						yield return _Tags.Add(c);
					}
				}
			}
        }

		TagSpan<ClassificationTag> TagComments(string className, SnapshotSpan snapshotSpan, IMappingTagSpan<IClassificationTag> tagSpan) {
			// find spans that the language service has already classified as comments ...
			if (className.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) == -1) {
				return null;
			}

			var text = snapshotSpan.GetText();
			//NOTE: markup comment span does not include comment start token
			var endOfCommentToken = 0;
			foreach (string t in _CodeType == CodeType.CSharp ? __CSharpComments : __Comments) {
				if (text.StartsWith(t, StringComparison.OrdinalIgnoreCase)) {
					endOfCommentToken = t.Length;
					break;
				}
			}

			if (endOfCommentToken == 0 && _CodeType != CodeType.Markup) {
				return null;
			}

			var tl = text.Length;
			var commentStart = endOfCommentToken;
			while (commentStart < tl) {
				if (Char.IsWhiteSpace(text[commentStart])) {
					++commentStart;
				}
				else {
					break;
				}
			}

			//TODO: code type context-awared end of comment
			var endOfContent = tl;
			if (_CodeType == CodeType.Markup && commentStart > 0) {
				if (!text.EndsWith("-->", StringComparison.Ordinal)) {
					return null;
				}

				endOfContent -= 3;
			}
			else if (text.StartsWith("/*", StringComparison.Ordinal)) {
				endOfContent -= 2;
			}

			ClassificationTag ctag = null;
			CommentLabel label = null;
			var startOfContent = 0;
			foreach (var item in Config.Instance.Labels) {
				startOfContent = commentStart + item.LabelLength;
				if (startOfContent >= tl
					|| text.IndexOf(item.Label, commentStart, item.Comparison) != commentStart) {
					continue;
				}

				var followingChar = text[commentStart + item.LabelLength];
				if (item.AllowPunctuationDelimiter && Char.IsPunctuation(followingChar)) {
					startOfContent++;
				}
				else if (!Char.IsWhiteSpace(followingChar)) {
					continue;
				}

				ctag = __CommentClassifications[(int)item.StyleID];
				label = item;
				break;
			}

			if (startOfContent == 0 || ctag == null) {
				return null;
			}

			// ignore whitespaces in content
			while (startOfContent < tl) {
				if (Char.IsWhiteSpace(text, startOfContent)) {
					++startOfContent;
				}
				else {
					break;
				}
			}
			while (endOfContent > startOfContent) {
				if (Char.IsWhiteSpace(text, endOfContent - 1)) {
					--endOfContent;
				}
				else {
					break;
				}
			}

			var span = label.StyleApplication == CommentStyleApplication.Tag
				? new SnapshotSpan(snapshotSpan.Snapshot, snapshotSpan.Start + commentStart, label.LabelLength)
				: label.StyleApplication == CommentStyleApplication.Content
				? new SnapshotSpan(snapshotSpan.Snapshot, snapshotSpan.Start + startOfContent, endOfContent - startOfContent)
				: new SnapshotSpan(snapshotSpan.Snapshot, snapshotSpan.Start + commentStart, endOfContent - commentStart);
			return new TagSpan<ClassificationTag>(span, ctag);
		}

        internal static CodeType GetCodeType(IContentType contentType)
        {
			return contentType.IsOfType("CSharp") ? CodeType.CSharp
				: contentType.IsOfType("html") || contentType.IsOfType("htmlx") || contentType.IsOfType("XAML") || contentType.IsOfType("XML") ? CodeType.Markup
				: CodeType.None;
        }

		static bool Matches(SnapshotSpan span, string text) {
			if (span.Length < text.Length) {
				return false;
			}
			int start = span.Start;
			int end = span.End;
			var s = span.Snapshot;
			// the span can contain white spaces at the start or at the end, skip them
			while (Char.IsWhiteSpace(s[--end]) && end > 0) {
			}
			while (Char.IsWhiteSpace(s[start]) && start < end) {
				start++;
			}
			if (++end - start != text.Length) {
				return false;
			}
			for (int i = start, ti = 0; i < end; i++, ti++) {
				if (s[i] != text[ti]) {
					return false;
				}
			}
			return true;
		}

		void AggregateorBatchedTagsChanged(object sender, EventArgs args) {
			if (Margin != null) {
				Margin.InvalidateVisual();
			}
		}

		#region IDisposable Support
		private bool disposedValue = false;

		void Dispose(bool disposing) {
			if (!disposedValue) {
				if (disposing) {
					_Aggregator.BatchedTagsChanged -= AggregateorBatchedTagsChanged;
				}
				disposedValue = true;
			}
		}

		public void Dispose() {
			Dispose(true);
		}
		#endregion
	}

}
