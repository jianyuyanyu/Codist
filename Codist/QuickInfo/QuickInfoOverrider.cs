﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AppHelpers;
using Codist.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text.Editor;

namespace Codist.QuickInfo
{
	interface IQuickInfoOverrider
	{
		UIElement Control { get; }
		void SetDiagnostics(IList<Diagnostic> diagnostics);
		void ApplyClickAndGo(ISymbol symbol, IAsyncQuickInfoSession quickInfoSession);
		void OverrideDocumentation(UIElement docElement);
		void OverrideException(UIElement exceptionDoc);
	}

	static class QuickInfoOverrider
	{
		static readonly SolidColorBrush __HighlightBrush = SystemColors.HighlightBrush.Alpha(0.3);

		public static IQuickInfoOverrider CreateOverrider(IAsyncQuickInfoSession session) {
			return session.Properties.GetOrCreateSingletonProperty<Default>(()=> new Default());
		}

		public static void HoldQuickInfo(DependencyObject quickInfoItem, bool hold) {
			FindHolder(quickInfoItem)?.Hold(hold);
		}

		public static void DismissQuickInfo(DependencyObject quickInfoItem) {
			FindHolder(quickInfoItem)?.DismissAsync();
		}

		static IQuickInfoHolder FindHolder(DependencyObject quickInfoItem) {
			var items = quickInfoItem.GetParent<ItemsControl>(i => i.GetType().Name == "WpfToolTipItemsControl");
			// version 16.1 or above
			items = items.GetParent<ItemsControl>(i => i.GetType().Name == "WpfToolTipItemsControl") ?? items;
			return items.GetFirstVisualChild<StackPanel>(o => o is IQuickInfoHolder) as IQuickInfoHolder;
		}
		static void ApplyClickAndGo(ISymbol symbol, TextBlock description, IAsyncQuickInfoSession quickInfoSession) {
			var locs = symbol.DeclaringSyntaxReferences;
			if (symbol.Kind == SymbolKind.Namespace) {
				description.ToolTip = "Locations: " + locs.Length;
				description.MouseEnter += HookNamespaceSymbolEvents;
				return;
			}
			string path;
			description.UseDummyToolTip();
			if (locs.IsDefaultOrEmpty) {
				if (symbol.ContainingType != null) {
					// if the symbol is implicitly declared but its containing type is in source,
					// navigate to the containing type
					locs = symbol.ContainingType.DeclaringSyntaxReferences;
					if (locs.Length != 0) {
						symbol = symbol.ContainingType;
						goto ClickAndGo;
					}
				}
				var asm = symbol.GetAssemblyModuleName();
				if (asm != null) {
					path = asm;
					description.MouseEnter += HookMetaSymbolEvents;
				}
				return;
			}
		ClickAndGo:
			path = System.IO.Path.GetFileName(locs[0].SyntaxTree.FilePath);
			description.MouseEnter += HookEvents;

			void HookMetaSymbolEvents(object sender, MouseEventArgs e) {
				var s = sender as FrameworkElement;
				s.MouseEnter -= HookMetaSymbolEvents;

				s.ToolTipOpening += ShowToolTip;
				s.UseDummyToolTip();
				s.ContextMenuOpening += ShowContextMenu;
			}
			void HookNamespaceSymbolEvents(object sender, EventArgs e) {
				var s = sender as FrameworkElement;
				s.MouseEnter -= HookNamespaceSymbolEvents;
				((TextBlock)sender).Background = __HighlightBrush;
				s.Cursor = Cursors.Hand;
				s.MouseEnter += HighlightSymbol;
				s.MouseLeave += RemoveSymbolHighlight;
				s.MouseLeftButtonUp += ListLocations;
				s.ContextMenuOpening += ShowContextMenu;
				s.ContextMenuClosing += ReleaseQuickInfo;
			}
			void HookEvents(object sender, MouseEventArgs e) {
				var s = sender as FrameworkElement;
				s.MouseEnter -= HookEvents;
				((TextBlock)sender).Background = __HighlightBrush;
				s.Cursor = Cursors.Hand;
				s.ToolTipOpening += ShowToolTip;
				s.MouseEnter += HighlightSymbol;
				s.MouseLeave += RemoveSymbolHighlight;
				if (locs.Length == 1) {
					s.MouseLeftButtonUp += GoToSource;
				}
				else {
					s.MouseLeftButtonUp += ListLocations;
				}
				s.UseDummyToolTip();
				s.ContextMenuOpening += ShowContextMenu;
				s.ContextMenuClosing += ReleaseQuickInfo;
			}
			void GoToSource(object sender, MouseButtonEventArgs e) {
				symbol.GoToSource();
			}
			async void ListLocations(object sender, MouseButtonEventArgs e) {
				await quickInfoSession.DismissAsync();
				CSharpSymbolContextMenu.ShowLocations(symbol, SemanticContext.GetOrCreateSingetonInstance(quickInfoSession.TextView as IWpfTextView));
			}
			void ShowToolTip(object sender, ToolTipEventArgs e) {
				var t = sender as TextBlock;
				t.ToolTip = ShowSymbolLocation(symbol, path);
				t.ToolTipOpening -= ShowToolTip;
			}
			void HighlightSymbol(object sender, MouseEventArgs e) {
				((TextBlock)sender).Background = __HighlightBrush;
			}
			void RemoveSymbolHighlight(object sender, MouseEventArgs e) {
				((TextBlock)sender).Background = Brushes.Transparent;
			}
			void ShowContextMenu(object sender, ContextMenuEventArgs e) {
				var s = sender as FrameworkElement;
				if (s.ContextMenu == null) {
					var ctx = SemanticContext.GetHovered();
					SyncHelper.RunSync(() => ctx.UpdateAsync(default));
					var m = new CSharpSymbolContextMenu(ctx) {
						Symbol = symbol,
						SyntaxNode = symbol.GetSyntaxNode()
					};
					m.AddAnalysisCommands();
					m.Items.Add(new Separator());
					m.AddNodeCommands();
					m.AddSymbolCommands();
					m.AddTitleItem(symbol.GetOriginalName());
					m.ItemClicked += HideQuickInfo;
					s.ContextMenu = m;
				}
				HoldQuickInfo(s, true);
				s.ContextMenu.IsOpen = true;
			}
			void ReleaseQuickInfo(object sender, ContextMenuEventArgs e) {
				HoldQuickInfo(sender as DependencyObject, false);
			}
			void HideQuickInfo(object sender, RoutedEventArgs e) {
				DismissQuickInfo(description);
			}
		}

		static StackPanel ShowSymbolLocation(ISymbol symbol, string path) {
			var tooltip = new ThemedToolTip();
			tooltip.Title.Append(symbol.GetOriginalName(), true);
			var t = tooltip.Content
				.Append("defined in ")
				.Append(String.IsNullOrEmpty(path) ? "?" : path, true);
			if (symbol.IsMemberOrType() && symbol.ContainingNamespace != null) {
				t.Append("\nnamespace: ").Append(symbol.ContainingNamespace.ToDisplayString());
			}
			if (symbol.Kind == SymbolKind.Method) {
				var m = (symbol as IMethodSymbol).ReducedFrom;
				if (m != null) {
					t.Append("\nclass: ").Append(m.ContainingType.Name);
				}
			}
			return tooltip;
		}

		interface IQuickInfoHolder
		{
			void Hold(bool hold);
			System.Threading.Tasks.Task DismissAsync();
		}

		/// <summary>
		/// The overrider for VS 15.8 and above versions.
		/// </summary>
		/// <remarks>
		/// <para>The original implementation of QuickInfo locates at: Common7\IDE\CommonExtensions\Microsoft\Editor\Microsoft.VisualStudio.Platform.VSEditor.dll</para>
		/// <para>class: Microsoft.VisualStudio.Text.AdornmentLibrary.ToolTip.Implementation.WpfToolTipControl</para>
		/// </remarks>
		sealed class Default : IQuickInfoOverrider
		{
			readonly Overrider _Overrider;

			public Default() {
				_Overrider = new Overrider();
				if (Config.Instance.QuickInfoMaxHeight > 0 && Config.Instance.QuickInfoMaxWidth > 0) {
					_Overrider.LimitItemSize = true;
				}
			}

			public UIElement Control => _Overrider;

			public void ApplyClickAndGo(ISymbol symbol, IAsyncQuickInfoSession quickInfoSession) {
				_Overrider.ClickAndGoSymbol = symbol;
				_Overrider.QuickInfoSession = quickInfoSession;
			}

			public void OverrideDocumentation(UIElement docElement) {
				_Overrider.DocElement = docElement;
			}
			public void OverrideException(UIElement exceptionDoc) {
				_Overrider.ExceptionDoc = exceptionDoc;
			}

			public void SetDiagnostics(IList<Diagnostic> diagnostics) {
				_Overrider.Diagnostics = diagnostics;
			}

			sealed class Overrider : StackPanel, IInteractiveQuickInfoContent, IQuickInfoHolder
			{
				static readonly Thickness __DocPanelBorderMargin = new Thickness(0, 0, -9, 3);
				static readonly Thickness __DocPanelBorderPadding = new Thickness(0, 0, 9, 0);
				static readonly Thickness __TitlePanelMargin = new Thickness(0, 0, 30, 6);

				public ISymbol ClickAndGoSymbol;
				public bool LimitItemSize;
				public UIElement DocElement;
				public UIElement ExceptionDoc;
				public IList<Diagnostic> Diagnostics;
				public IAsyncQuickInfoSession QuickInfoSession;

				public bool KeepQuickInfoOpen { get; set; }
				public bool IsMouseOverAggregated { get; set; }

				public void Hold(bool hold) {
					IsMouseOverAggregated = hold;
				}
				public async System.Threading.Tasks.Task DismissAsync() {
					await QuickInfoSession.DismissAsync();
				}

				protected override void OnVisualParentChanged(DependencyObject oldParent) {
					base.OnVisualParentChanged(oldParent);
					var p = this.GetParent<StackPanel>();
					if (p == null) {
						goto EXIT;
					}
					if ((Config.Instance.DisplayOptimizations & DisplayOptimizations.CodeWindow) != 0) {
						WpfHelper.SetUITextRenderOptions(p, true);
					}
					if (p.Children.Count > 1) {
						OverrideDiagnosticInfo(p);
						p.SetValue(TextBlock.FontFamilyProperty, ThemeHelper.ToolTipFont);
						p.SetValue(TextBlock.FontSizeProperty, ThemeHelper.ToolTipFontSize);
					}
					if (DocElement != null || ExceptionDoc != null || ClickAndGoSymbol != null || LimitItemSize) {
						FixQuickInfo(p);
					}
					if (LimitItemSize) {
						ApplySizeLimit(this.GetParent<StackPanel>());
					}
					EXIT:
					// hides the parent container from taking excessive space in the quick info window
					this.GetParent<Border>().Collapse();
				}

				void OverrideDiagnosticInfo(StackPanel panel) {
					var infoPanel = panel.Children[1].GetFirstVisualChild<ItemsControl>()?.GetFirstVisualChild<StackPanel>();
					if (infoPanel == null) {
						// try the first item (symbol title may be absent)
						infoPanel = panel.Children[0].GetFirstVisualChild<ItemsControl>()?.GetFirstVisualChild<StackPanel>();
						if (infoPanel?.GetFirstVisualChild<WrapPanel>() != null) {
							return;
						}
					}
					if (infoPanel == null) {
						return;
					}
					foreach (var item in infoPanel.Children) {
						var cp = (item as UIElement).GetFirstVisualChild<TextBlock>();
						if (cp == null) {
							continue;
						}
						if (Diagnostics != null && Diagnostics.Count > 0) {
							var t = cp.GetText();
							var d = Diagnostics.FirstOrDefault(i => i.GetMessage() == t);
							if (d != null) {
								cp.UseDummyToolTip();
								cp.Tag = d;
								cp.SetGlyph(ThemeHelper.GetImage(GetGlyphForSeverity(d.Severity)));
								cp.ToolTipOpening += ShowToolTipForDiagnostics;
							}
						}
						else {
							cp.SetGlyph(ThemeHelper.GetImage(KnownImageIds.StatusInformation));
						}
						TextEditorWrapper.CreateFor(cp);
					}

					int GetGlyphForSeverity(DiagnosticSeverity severity) {
						switch (severity) {
							case DiagnosticSeverity.Warning: return KnownImageIds.StatusWarning;
							case DiagnosticSeverity.Error: return KnownImageIds.StatusError;
							case DiagnosticSeverity.Hidden: return KnownImageIds.StatusHidden;
							default: return KnownImageIds.StatusInformation;
						}
					}

					void ShowToolTipForDiagnostics(object sender, ToolTipEventArgs e) {
						var t = sender as TextBlock;
						var d = t.Tag as Diagnostic;
						var tip = new ThemedToolTip();
						tip.Title.Append(d.Descriptor.Category + " (" + d.Id + ")", true);
						tip.Content.Append(d.Descriptor.Title.ToString());
						if (String.IsNullOrEmpty(d.Descriptor.HelpLinkUri) == false) {
							tip.Content.AppendLine().Append("Help: " + d.Descriptor.HelpLinkUri);
						}
						if (d.IsSuppressed) {
							tip.Content.AppendLine().Append("Suppressed");
						}
						if (d.IsWarningAsError) {
							tip.Content.AppendLine().Append("Content as error");
						}
						t.ToolTip = tip;
						t.ToolTipOpening -= ShowToolTipForDiagnostics;
					}
				}

				void FixQuickInfo(StackPanel infoPanel) {
					var titlePanel = infoPanel.GetFirstVisualChild<WrapPanel>();
					if (titlePanel == null) {
						return;
					}
					var doc = titlePanel.GetParent<StackPanel>();
					if (doc == null) {
						return;
					}
					var v16_1orLater = titlePanel.GetParent<ItemsControl>().GetParent<ItemsControl>() != null;

					titlePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
					doc.HorizontalAlignment = HorizontalAlignment.Stretch;

					var icon = infoPanel.GetFirstVisualChild<CrispImage>();
					var signature = infoPanel.GetFirstVisualChild<TextBlock>();

					// beautify the title panel
					if (Config.Instance.QuickInfoOptions.MatchFlags(QuickInfoOptions.AlternativeStyle)) {
						if (icon != null) {
							infoPanel.GetParent<Border>().Background = new VisualBrush(CreateEnlargedIcon(icon)) {
								Opacity = 0.3,
								AlignmentX = AlignmentX.Right,
								AlignmentY = AlignmentY.Bottom,
								TileMode = TileMode.None,
								Stretch = Stretch.None
							};
							icon.Visibility = Visibility.Collapsed;
						}
						var c = infoPanel.GetParent<Border>();
						c.Margin = __DocPanelBorderMargin;
						c.Padding = __DocPanelBorderPadding;
						c.MinHeight = 50;
						titlePanel.Margin = __TitlePanelMargin;
					}

					// replace the default XML doc
					// sequence of items in default XML Doc panel:
					// 1. summary
					// 2. type parameter
					// 3. usage
					// 4. exception
					var items = doc.IsItemsHost ? (IList)doc.GetParent<ItemsControl>().Items : doc.Children;
					if (DocElement != null) {
						OverrideDocElement(doc, v16_1orLater, items);
					}
					if (ExceptionDoc != null) {
						OverrideExceptionDocElement(doc, v16_1orLater, items);
					}

					if (icon != null && signature != null) {
						// apply click and go feature
						if (ClickAndGoSymbol != null) {
							QuickInfoOverrider.ApplyClickAndGo(ClickAndGoSymbol, signature, QuickInfoSession);
						}
						// fix the width of the signature part to prevent it from falling down to the next row
						if (Config.Instance.QuickInfoMaxWidth > 0) {
							signature.MaxWidth = Config.Instance.QuickInfoMaxWidth - icon.Width - 30;
						}
					}
				}

				void OverrideDocElement(StackPanel doc, bool v16_1orLater, IList items) {
					try {
						if (items.Count > 1 && items[1] is TextBlock) {
							items.RemoveAt(1);
							items.Insert(1, DocElement);
						}
						else {
							items.Add(DocElement);
						}
						var myDoc = DocElement as ThemedTipDocument;
						if (myDoc == null) {
							return;
						}
						if (v16_1orLater && myDoc.Tag is int) {
							// in v16.1 or later, 2nd and following paragraphs in XML Doc are in an outer ItemsControl
							items = doc.GetParent<ItemsControl>()?.Items;
							if (items != null) {
								// used the value from XmlDocRenderer.ParagraphCount to remove builtin paragraphs
								for (int i = Math.Min(items.Count - 1, (int)myDoc.Tag) - 1; i >= 0; i--) {
									if (items[1] is TextBlock == false) {
										break;
									}
									items.RemoveAt(1);
								}
							}
						}
						myDoc.ApplySizeLimit();
					}
					catch (InvalidOperationException) {
						// ignore exception: doc.Children was changed by another thread
					}
				}

				void OverrideExceptionDocElement(StackPanel doc, bool v16_1orLater, IList items) {
					if (v16_1orLater) {
						items = doc.GetParent<ItemsControl>().Items;
					}
					try {
						if (items.Count > 1) {
							for (int i = items.Count - 1; i > 0; i--) {
								var item = items[i];
								if (v16_1orLater && item is StackPanel && item is ThemedTipDocument == false || item is TextBlock) {
									items.RemoveAt(i);
									break;
								}
							}
						}
						items.Add(ExceptionDoc);
						//todo move this to ApplySizeLimit
						(ExceptionDoc as ThemedTipDocument)?.ApplySizeLimit();
					}
					catch (InvalidOperationException) {
						// ignore exception: doc.Children was changed by another thread
					}
				}

				static CrispImage CreateEnlargedIcon(CrispImage icon) {
					var bgIcon = new CrispImage { Moniker = icon.Moniker, Width = 48, Height = 48 };
					bgIcon.SetBackgroundForCrispImage(ThemeHelper.TitleBackgroundColor);
					return bgIcon;
				}

				static void ApplySizeLimit(StackPanel quickInfoPanel) {
					if (quickInfoPanel == null) {
						return;
					}
					var docPanel = quickInfoPanel.Children[0].GetFirstVisualChild<WrapPanel>().GetParent<StackPanel>();
					var docPanelHandled = docPanel == null; // don't process docPanel if it is not found
					foreach (var item in quickInfoPanel.Children) {
						var o = item as DependencyObject;
						if (o == null) {
							continue;
						}
						var cp = o.GetFirstVisualChild<ContentPresenter>();
						if (cp == null) {
							continue;
						}
						var c = cp.Content;
						if (c is Overrider || c is IInteractiveQuickInfoContent /* don't hack interactive content */) {
							continue;
						}
						if (docPanel == c || docPanelHandled == false && cp.GetFirstVisualChild<StackPanel>(i => i == docPanel) != null) {
							cp.LimitSize();
							if (Config.Instance.QuickInfoXmlDocExtraHeight > 0 && Config.Instance.QuickInfoMaxHeight > 0) {
								cp.MaxHeight += Config.Instance.QuickInfoXmlDocExtraHeight;
							}
							foreach (var r in docPanel.Children) {
								(r as ThemedTipDocument)?.ApplySizeLimit();
							}
							c = cp.Content;
							cp.Content = null;
							cp.Content = ((DependencyObject)c).Scrollable();
							docPanelHandled = true;
							continue;
						}
						else if (c is StackPanel s) {
							MakeChildrenScrollable(s);
							continue;
						}
						(c as ThemedTipDocument)?.ApplySizeLimit();
						if (c is ScrollViewer) {
							continue;
						}
						var v = c as IWpfTextView; // snippet tooltip, some other default tooltip
						if (v != null) {
							cp.Content = new ThemedTipText {
								Text = v.TextSnapshot.GetText()
							}.Scrollable();
							//v.VisualElement.LimitSize();
							//v.Options.SetOptionValue("TextView/WordWrapStyle", WordWrapStyles.WordWrap);
							//v.Options.SetOptionValue("TextView/AutoScroll", true);
							continue;
						}
						o = c as DependencyObject;
						if (o == null) {
							var s = c as string;
							if (s != null) {
								cp.Content = new ThemedTipText {
									Text = s
								}.Scrollable();
							}
							continue;
						}
						cp.Content = null;
						cp.Content = o.Scrollable();
					}
				}

				static void MakeChildrenScrollable(StackPanel s) {
					var children = new List<DependencyObject>(s.Children.Count);
					foreach (DependencyObject n in s.Children) {
						children.Add(n);
					}
					s.Children.Clear();
					foreach (var c in children) {
						var d = c as ThemedTipDocument;
						if (d != null) {
							foreach (var item in d.Children) {
								(item as FrameworkElement)?.LimitSize();
								if (item is TextBlock t) {
									t.TextWrapping = TextWrapping.Wrap;
								}
							}
							d.ApplySizeLimit();
							d.WrapMargin(WpfHelper.SmallVerticalMargin);
						}
						s.Add(c.Scrollable().LimitSize());
					}
				}
			}
		}
	}
}
