﻿using System;
using System.Linq;
using CLR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using R = Codist.Properties.Resources;

namespace Codist.Refactorings
{
	abstract class ReplaceText : IRefactoring
	{
		public static readonly ReplaceText WrapInRegionDirective = new WrapInTextRefactoring(R.CMD_SurroundWithRegion, "#region RegionName", "#endregion", 8/*lengthof("#region ")*/, 10/*lengthof(RegionName)*/);
		public static readonly ReplaceText WrapInIfDirective = new WrapInTextRefactoring(R.CMD_SurroundWithIf, "#if DEBUG", "#endif", 4/*lengthof("#if ")*/, 5/*lengthof(DEBUG)*/);
		public static readonly ReplaceText CommentToRegion = new CommentToRegionRefactoring();
		public static readonly ReplaceText SealType = new SealTypeRefactoring();
		public static readonly ReplaceText MakeStatic = new StaticRefactoring();
		public static readonly ReplaceText MakeReadonly = new ReadonlyFieldRefactoring();
		public static readonly ReplaceText MakePublic = new ChangeAccessibilityRefactoring(SyntaxKind.PublicKeyword);
		public static readonly ReplaceText MakeProtected = new ChangeAccessibilityRefactoring(SyntaxKind.ProtectedKeyword);
		public static readonly ReplaceText MakeInternal = new ChangeAccessibilityRefactoring(SyntaxKind.InternalKeyword);
		public static readonly ReplaceText MakePrivate = new ChangeAccessibilityRefactoring(SyntaxKind.PrivateKeyword);

		public abstract int IconId { get; }
		public abstract string Title { get; }

		public abstract bool Accept(RefactoringContext context);

		public abstract void Refactor(SemanticContext context);

		static string GetLineBreakText(ITextSnapshotLine line, ITextView view) {
			return line.GetLineBreakText()
				?? view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);
		}

		sealed class WrapInTextRefactoring : ReplaceText
		{
			readonly string _Title;
			readonly string _Start, _End;
			readonly int _SelectStart, _SelectLength;

			public WrapInTextRefactoring(string title, string start, string end, int selectStart, int selectLength) {
				_Title = title;
				_Start = start;
				_End = end;
				_SelectStart = selectStart;
				_SelectLength = selectLength;
			}
			public override int IconId => IconIds.SurroundWith;
			public override string Title => _Title;

			public override bool Accept(RefactoringContext ctx) {
				var v = ctx.SemanticContext.View;
				var s = v.Selection;
				if (s.IsEmpty || s.Mode != TextSelectionMode.Stream) {
					return false;
				}
				var sn = v.TextSnapshot;
				int ss = s.Start.Position.Position, se;
				ITextSnapshotLine ls = sn.GetLineFromPosition(ss), le;
				var p1 = ss - ls.Start.Position;
				var w = ls.CountLinePrecedingWhitespace();
				if (p1 >= 0 && p1 <= w) {
					se = s.End.Position.Position;
					le = sn.GetLineFromPosition(se > 0 ? se - 1 : se);
					if (le.EndIncludingLineBreak.Position == se || le.End.Position == se) {
						return IsWhitespaceTrivia(ctx.SemanticContext.Compilation.FindTrivia(ss)) && IsWhitespaceTrivia(ctx.SemanticContext.Compilation.FindTrivia(se));
					}
				}
				return false;
			}

			public override void Refactor(SemanticContext ctx) {
				var sl = ctx.View.TextSnapshot.GetLineFromPosition(ctx.View.Selection.Start.Position);
				var sp = sl.Start.Position;
				var indent = sl.GetLinePrecedingWhitespace();
				ctx.View.Edit(ctx, (v, p, edit) => {
					var s = v.Selection;
					int se = s.End.Position.Position;
					var le = v.TextSnapshot.GetLineFromPosition(se - 1);
					var newLine = GetLineBreakText(sl, v);
					edit.Insert(sl.Start.Position, indent + _Start + newLine);
					edit.Insert(le.EndIncludingLineBreak.Position, indent + _End + newLine);
				});
				ctx.View.SelectSpan(sp + indent.Length + _SelectStart, _SelectLength, 1);
			}

			static bool IsWhitespaceTrivia(SyntaxTrivia trivia) {
				return trivia.RawKind.CeqAny(0, (int)SyntaxKind.WhitespaceTrivia, (int)SyntaxKind.EndOfLineTrivia);
			}
		}

		sealed class CommentToRegionRefactoring : ReplaceText
		{
			static readonly char[] __LeadingCommentChars = new[] { '/', ' ', '\t' };
			public override int IconId => IconIds.Region;
			public override string Title => R.CMD_CommentToRegion;

			public override bool Accept(RefactoringContext ctx) {
				var statements = ctx.SelectedStatementInfo.Items;
				return statements?[0].HasLeadingTrivia == true
					&& GetSoloSingleLineComment(statements[0].GetLeadingTrivia()).IsKind(SyntaxKind.SingleLineCommentTrivia);
			}

			public override void Refactor(SemanticContext ctx) {
				const int LENGTH_OF_REGION = 8;
				var comment = GetSoloSingleLineComment(new RefactoringContext(ctx).SelectedStatementInfo.Items[0].GetLeadingTrivia());
				if (comment.FullSpan.Length == 0) {
					return;
				}
				var commentText = comment.ToFullString().TrimStart(__LeadingCommentChars);
				ctx.View.Edit(new { comment, commentText }, (v, p, edit) => {
					var s = v.Selection;
					var sl = v.TextSnapshot.GetLineFromPosition(v.Selection.Start.Position);
					var indent = sl.GetLinePrecedingWhitespace();
					var newLine = GetLineBreakText(sl, v);
					int se = s.End.Position.Position;
					var le = v.TextSnapshot.GetLineFromPosition(se - 1); 
					edit.Replace(p.comment.FullSpan.ToSpan(), "#region " + p.commentText);
					edit.Insert(le.End, newLine + indent + "#endregion" + newLine);
				});
				ctx.View.SelectSpan(comment.SpanStart + LENGTH_OF_REGION, commentText.Length, 1);
			}

			static SyntaxTrivia GetSoloSingleLineComment(SyntaxTriviaList trivias) {
				const int START = 0, COMMENT = 1, EOL = 2;
				var s = START;
				SyntaxTrivia comment = default;
				foreach (var trivia in trivias) {
					var k = trivia.Kind();
					switch (s) {
						case START:
							switch (k) {
								case SyntaxKind.WhitespaceTrivia:
								case SyntaxKind.EndOfLineTrivia:
									continue;
								case SyntaxKind.SingleLineCommentTrivia:
									s = COMMENT;
									comment = trivia;
									continue;
							}
							goto default;
						case COMMENT:
							if (k == SyntaxKind.EndOfLineTrivia) {
								s = EOL;
								continue;
							}
							goto default;
						case EOL:
							if (k.CeqAny(SyntaxKind.EndOfLineTrivia, SyntaxKind.WhitespaceTrivia)) {
								continue;
							}
							goto default;
						default:
							return default;
					}
				}
				return s == EOL ? comment : default;
			}
		}

		abstract class DeclarationModifierRefactoring : ReplaceText
		{
			protected static MemberDeclarationSyntax GetDeclarationNode(SemanticContext ctx) {
				var node = ctx.Node;
				if (node.IsKind(SyntaxKind.VariableDeclarator)) {
					node = node.Parent.Parent;
				}
				return node as MemberDeclarationSyntax;
			}

			protected static int GetModifierInsertionPoint(MemberDeclarationSyntax node) {
				var attrList = node.GetAttributes(out _);
				return attrList.Count != 0
					? attrList[attrList.Count - 1].FullSpan.End
					: node.SpanStart;
			}
		}

		sealed class SealTypeRefactoring : DeclarationModifierRefactoring
		{
			string _Title;
			public override int IconId => IconIds.SealedClass;
			public override string Title => _Title;

			public override bool Accept(RefactoringContext ctx) {
				var node = ctx.Node;
				if (node.IsAnyKind(SyntaxKind.ClassDeclaration, CodeAnalysisHelper.RecordDeclaration)
					&& CanBeSealed(((TypeDeclarationSyntax)node).Modifiers)) {
					_Title = node.IsKind(CodeAnalysisHelper.RecordDeclaration)
						? R.CMD_SealRecord
						: R.CMD_SealClass;
					return true;
				}
				return false;
			}

			static bool CanBeSealed(SyntaxTokenList modifiers) {
				foreach (var item in modifiers) {
					switch (item.Kind()) {
						case SyntaxKind.SealedKeyword:
						case SyntaxKind.AbstractKeyword:
						case SyntaxKind.StaticKeyword:
						case SyntaxKind.VirtualKeyword:
							return false;
					}
				}
				return true;
			}

			public override void Refactor(SemanticContext ctx) {
				const int LENGTH_OF_SEALED = 6;
				var d = ctx.Node as TypeDeclarationSyntax;
				var m = d.Modifiers;
				var insertAt = m.FullSpan.Length == 0 ? d.SpanStart
					: m[0].IsAnyKind(SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword) ? m[0].FullSpan.End
					: GetModifierInsertionPoint(d);
				ctx.View.Edit(insertAt, (view, param, edit) => {
					edit.Insert(param, "sealed ");
				});
				ctx.View.SelectSpan(insertAt, LENGTH_OF_SEALED, 1);
			}
		}


		sealed class ReadonlyFieldRefactoring : DeclarationModifierRefactoring
		{
			public override int IconId => IconIds.ReadonlyField;
			public override string Title => R.CMD_MakeReadonly;

			public override bool Accept(RefactoringContext ctx) {
				var node = ctx.Node;
				if (node.IsKind(SyntaxKind.VariableDeclarator) == false) {
					return false;
				}
				node = node.Parent.Parent;
				return node.IsAnyKind(SyntaxKind.FieldDeclaration, SyntaxKind.EventFieldDeclaration)
					&& CanBeReadonly(((BaseFieldDeclarationSyntax)node).Modifiers);
			}

			static bool CanBeReadonly(SyntaxTokenList modifiers) {
				foreach (var item in modifiers) {
					switch (item.Kind()) {
						case SyntaxKind.ReadOnlyKeyword:
						case SyntaxKind.ConstKeyword:
						case SyntaxKind.VolatileKeyword:
							return false;
					}
				}
				return true;
			}

			public override void Refactor(SemanticContext ctx) {
				const int LENGTH_OF_READONLY = 8;
				var node = ctx.Node;
				if (node.IsKind(SyntaxKind.VariableDeclarator) == false) {
					return;
				}
				var d = node.Parent.Parent as BaseFieldDeclarationSyntax;
				if (d == null) {
					return;
				}
				var m = d.Modifiers;
				var insertAt = m.FullSpan.Length == 0 ? d.SpanStart
					: m[0].IsAnyKind(SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.StaticKeyword) ? m[0].FullSpan.End
					: GetModifierInsertionPoint(d);
				ctx.View.Edit(insertAt, (view, param, edit) => {
					edit.Insert(param, "readonly ");
				});
				ctx.View.SelectSpan(insertAt, LENGTH_OF_READONLY, 1);
			}
		}

		sealed class StaticRefactoring : DeclarationModifierRefactoring
		{
			public override int IconId => IconIds.StaticMember;
			public override string Title => R.CMD_MakeStatic;

			public override bool Accept(RefactoringContext ctx) {
				if (GetDeclarationNode(ctx.SemanticContext) is MemberDeclarationSyntax d) {
					var m = d.GetModifiers(out var canHaveModifier);
					TypeDeclarationSyntax t;
					return canHaveModifier
						&& d.IsAnyKind(SyntaxKind.ConstructorDeclaration, SyntaxKind.DestructorDeclaration, CodeAnalysisHelper.RecordDeclaration, CodeAnalysisHelper.RecordStructDeclaration) == false
						&& ((t = d as TypeDeclarationSyntax) == null
							|| t.GetParameterList() == null) // exclude primary constructor
						&& CanBeStatic(m);
				}
				return false;
			}

			static bool CanBeStatic(SyntaxTokenList modifiers) {
				foreach (var item in modifiers) {
					switch (item.Kind()) {
						case SyntaxKind.StaticKeyword:
						case SyntaxKind.OverrideKeyword:
						case SyntaxKind.SealedKeyword:
						case SyntaxKind.VirtualKeyword:
						case SyntaxKind.AbstractKeyword:
							return false;
					}
				}
				return true;
			}

			public override void Refactor(SemanticContext ctx) {
				const int LENGTH_OF_STATIC = 6;
				var d = GetDeclarationNode(ctx);
				if (d == null) {
					return;
				}
				var m = d.GetModifiers(out var canHaveModifier);
				var insertAt = m.FullSpan.Length == 0 ? d.SpanStart
					: m[0].IsAnyKind(SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword) ? m[0].FullSpan.End
					: GetModifierInsertionPoint(d);
				ctx.View.Edit(insertAt, (view, param, edit) => {
					edit.Insert(param, "static ");
				});
				ctx.View.SelectSpan(insertAt, LENGTH_OF_STATIC, 1);
			}
		}

		sealed class ChangeAccessibilityRefactoring : DeclarationModifierRefactoring
		{
			readonly SyntaxKind _KeywordKind;
			readonly int _IconId;
			readonly string _Title;

			public override int IconId => _IconId;
			public override string Title => _Title;

			public ChangeAccessibilityRefactoring(SyntaxKind accessibility) {
				switch (_KeywordKind = accessibility) {
					case SyntaxKind.PublicKeyword:
						_IconId = IconIds.PublicSymbols;
						_Title = R.CMD_MakePublic;
						break;
					case SyntaxKind.ProtectedKeyword:
						_IconId = IconIds.ProtectedSymbols;
						_Title = R.CMD_MakeProtected;
						break;
					case SyntaxKind.InternalKeyword:
						_IconId = IconIds.InternalSymbols;
						_Title = R.CMD_MakeInternal;
						break;
					case SyntaxKind.PrivateKeyword:
						_IconId = IconIds.PrivateSymbols;
						_Title = R.CMD_MakePrivate;
						break;
				}
			}

			public override bool Accept(RefactoringContext ctx) {
				var node = GetDeclarationNode(ctx.SemanticContext);
				return node != null && CanChangeAccessibility(node);
			}

			bool CanChangeAccessibility(MemberDeclarationSyntax d) {
				var m = d.GetModifiers(out var canHaveModifier);
				if (canHaveModifier == false
					|| m.Any(_KeywordKind)
					|| m.Any(SyntaxKind.OverrideKeyword)
					|| d.IsKind(SyntaxKind.EnumMemberDeclaration)) {
					return false;
				}
				switch (_KeywordKind) {
					case SyntaxKind.PublicKeyword:
					case SyntaxKind.InternalKeyword:
						return true;
					case SyntaxKind.ProtectedKeyword:
						return m.Any(SyntaxKind.SealedKeyword) == false
							&& d.Parent is ClassDeclarationSyntax c
							&& c.Modifiers.Any(SyntaxKind.SealedKeyword) == false;
					case SyntaxKind.PrivateKeyword:
						if (d is BaseTypeDeclarationSyntax t
							&& t.IsKind(SyntaxKind.InterfaceDeclaration) == false) {
							return d.Parent is BaseTypeDeclarationSyntax;
						}
						return true;
				}
				return true;
			}

			public override void Refactor(SemanticContext ctx) {
				var d = GetDeclarationNode(ctx);
				SyntaxTokenList modifiers = d.GetModifiers(out var canHaveModifier);
				if (canHaveModifier == false) {
					return;
				}

				var modifier = GetModifier(_KeywordKind);
				if (modifiers.Count != 0 && ctx.View.Edit((modifiers, modifier), (view, param, edit) => {
					var replaced = false;
					Span span;
					foreach (var item in param.modifiers) {
						switch (item.Kind()) {
							case SyntaxKind.PublicKeyword:
							case SyntaxKind.ProtectedKeyword:
							case SyntaxKind.InternalKeyword:
							case SyntaxKind.PrivateKeyword:
								if (replaced == false) {
									replaced = edit.Replace(span = item.Span.ToSpan(), param.modifier);
									view.SelectSpan(span);
								}
								else {
									var firstTrailing = item.TrailingTrivia.FirstOrDefault();
									if (firstTrailing.IsKind(SyntaxKind.WhitespaceTrivia)) {
										var s = item.Span;
										span = new Span(s.Start, s.Length + firstTrailing.FullSpan.Length);
									}
									else {
										span = item.Span.ToSpan();
									}
									edit.Replace(span, String.Empty);
								}
								break;
						}
					}
				}) != null) {
					ctx.View.SelectSpan(ctx.View.GetCaretPosition().Position - modifier.Length, modifier.Length, 0);
					return;
				}

				var tp = modifiers.Count != 0
					? modifiers.Span.Start
					: GetModifierInsertionPoint(d);
				ctx.View.Edit((tp, modifier), (view, param, edit) => edit.Insert(param.tp, param.modifier + " "));
				ctx.View.SelectSpan(tp, modifier.Length, 1);
			}

			static string GetModifier(SyntaxKind kind) {
				switch (kind) {
					case SyntaxKind.PublicKeyword: return "public";
					case SyntaxKind.InternalKeyword: return "internal";
					case SyntaxKind.ProtectedKeyword: return "protected";
					case SyntaxKind.PrivateKeyword: return "private";
				}
				return String.Empty;
			}
		}
	}
}