﻿using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppHelpers;
using Microsoft.CodeAnalysis;

namespace Codist.Controls
{
	sealed class SymbolItem : INotifyPropertyChanged
	{
		UIElement _Icon;
		int _ImageId;
		TextBlock _Content;
		string _Hint;
		readonly bool _IncludeContainerType;
		internal SyntaxNode SyntaxNode { get; private set; }
		internal ISymbol Symbol { get; private set; }
		internal SymbolList Container { get; private set; }
		internal Location Location { get; set; }

		// the following properties must be public since they are also used in WPF
		public SymbolUsageKind Usage { get; set; }

		public int ImageId => _ImageId != 0 ? _ImageId : (_ImageId = Symbol != null ? Symbol.GetImageId() : SyntaxNode != null ? SyntaxNode.GetImageId() : -1);
		public UIElement Icon => _Icon ?? (_Icon = Container?.IconProvider?.Invoke(this) ?? ThemeHelper.GetImage(ImageId != -1 ? ImageId : 0));
		public UIElement ExtIcon => Container?.ExtIconProvider?.Invoke(this);
		public string Hint {
			get => _Hint ?? (_Hint = Symbol != null && Container != null && Symbol.Kind == SymbolKind.Field ? GetSymbolConstantValue(Symbol, Container.ContainerType == SymbolListType.EnumFlags) : String.Empty);
			set => _Hint = value;
		}
		public bool IsExternal => Usage == SymbolUsageKind.External
			|| Container != null && Container.ContainerType == SymbolListType.None && Symbol?.ContainingAssembly.GetSourceType() == AssemblySource.Metadata;
		public TextBlock Content {
			get => _Content ?? (_Content = Symbol != null
				? CreateContentForSymbol(Symbol, _IncludeContainerType, true)
				: SyntaxNode != null
					? new ThemedMenuText().Append(SyntaxNode.GetDeclarationSignature(), SymbolFormatter.Instance.GetBrush(SyntaxNode))
					: new ThemedMenuText());
			set => _Content = value;
		}

		public SymbolItem(SymbolList list) {
			Container = list;
			Content = new ThemedMenuText();
			_ImageId = -1;
		}
		public SymbolItem(Location location, SymbolList list) {
			Container = list;
			Location = location;
			if (location.IsInSource) {
				var filePath = location.SourceTree.FilePath;
				_Content = new ThemedMenuText(Path.GetFileNameWithoutExtension(filePath)).Append(Path.GetExtension(filePath), ThemeHelper.SystemGrayTextBrush);
				_Hint = Path.GetFileName(Path.GetDirectoryName(filePath));
				_ImageId = IconIds.FileEmpty;
			}
			else {
				var m = location.MetadataModule;
				_Content = new ThemedMenuText(Path.GetFileNameWithoutExtension(m.Name)).Append(Path.GetExtension(m.Name), ThemeHelper.SystemGrayTextBrush);
				_Hint = String.Empty;
				_ImageId = IconIds.Module;
			}
		}
		public SymbolItem(ISymbol symbol, SymbolList list, ISymbol containerSymbol)
			: this (symbol, list, false) {
			_ImageId = containerSymbol.GetImageId();
			_Content = CreateContentForSymbol(containerSymbol, false, true);
		}
		public SymbolItem(ISymbol symbol, SymbolList list, bool includeContainerType) {
			Symbol = symbol;
			Container = list;
			_IncludeContainerType = includeContainerType;
		}

		public SymbolItem(SyntaxNode node, SymbolList list) {
			SyntaxNode = node;
			Container = list;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public bool GoToSource() {
			if (Location?.IsInSource == true) {
				GoToLocation();
				return true;
			}
			if (SyntaxNode != null) {
				GoToNode();
				return true;
			}
			if (Symbol != null) {
				return GoToSymbol();
			}
			return false;
		}

		void GoToLocation() {
			var loc = Location;
			CloseUnpinnedMenus();
			loc.GoToSource();
		}

		void GoToNode() {
			RefreshSyntaxNode();
			var node = SyntaxNode;
			CloseUnpinnedMenus();
			node.GetIdentifierToken().GetLocation().GoToSource();
		}

		bool GoToSymbol() {
			RefreshSymbol();
			if (Symbol.Kind == SymbolKind.Namespace) {
				SyncHelper.RunSync(() => Container.SemanticContext.FindMembersAsync(Symbol, _Content.GetParent<ListBoxItem>().NullIfMouseOver()));
				return false;
			}
			var s = Symbol.GetSourceReferences();
			switch (s.Length) {
				case 0:
					if (Container.SemanticContext.Document != null) {
						var symbol = Symbol;
						var proj = Container.SemanticContext.Document.Project;
						CloseUnpinnedMenus();
						return ServicesHelper.Instance.VisualStudioWorkspace.TryGoToDefinition(symbol, proj, default);
					}
					return false;
				case 1:
					CloseUnpinnedMenus();
					s[0].GoToSource();
					return true;
				default:
					Container.SemanticContext.ShowLocations(Symbol, s, _Content.GetParent<ListBoxItem>().NullIfMouseOver());
					return false;
			}
		}

		void CloseUnpinnedMenus() {
			if (_Content.GetParent<ListBoxItem>()?.IsMouseOver == false
				&& System.Windows.Input.Keyboard.Modifiers.MatchFlags(System.Windows.Input.ModifierKeys.Control) == false) {
				ExternalAdornment.Get(Container.SemanticContext.View)?.ClearUnpinnedChildren();
			}
		}

		public bool SelectIfContainsPosition(int position) {
			if (IsExternal || SyntaxNode == null || SyntaxNode.FullSpan.Contains(position, true) == false) {
				return false;
			}
			Container.SelectedValue = this;
			return true;
		}
		static ThemedMenuText CreateContentForSymbol(ISymbol symbol, bool includeType, bool includeParameter) {
			var t = new ThemedMenuText();
			if (includeType && symbol.ContainingType != null) {
				t.Append(symbol.ContainingType.Name + symbol.ContainingType.GetParameterString() + ".", ThemeHelper.SystemGrayTextBrush);
			}
			t.Append(symbol.GetOriginalName(), SymbolFormatter.Instance.GetBrush(symbol));
			if (includeParameter) {
				t.Append(symbol.GetParameterString(), ThemeHelper.SystemGrayTextBrush);
			}
			return t;
		}

		static string GetSymbolConstantValue(ISymbol symbol, bool useHexBin) {
			var f = (IFieldSymbol)symbol;
			if (f.HasConstantValue) {
				return useHexBin && f.ConstantValue is IFormattable v ? "0x" + v.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) : f.ConstantValue?.ToString();
			}
			return null;
		}
		internal void SetSymbolToSyntaxNode() {
			Symbol = SyncHelper.RunSync(() => Container.SemanticContext.GetSymbolAsync(SyntaxNode));
		}
		internal void RefreshSyntaxNode() {
			var node = Container.SemanticContext.RelocateDeclarationNode(SyntaxNode);
			if (node != null && node != SyntaxNode) {
				SyntaxNode = node;
			}
		}
		internal void RefreshSymbol() {
			if (Symbol.ContainingAssembly.GetSourceType() != AssemblySource.Metadata) {
				var symbol = Container.SemanticContext.RelocateSymbolAsync(Symbol).GetAwaiter().GetResult();
				if (symbol != null && symbol != Symbol) {
					Symbol = symbol;
				}
			}
		}

		public override string ToString() {
			return Content.GetText();
		}

		internal void Release() {
			Symbol = null;
			Location = null;
			SyntaxNode = null;
			Container = null;
		}
	}
}
