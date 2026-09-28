package com.adamantium.auml

import com.intellij.openapi.editor.DefaultLanguageHighlighterColors
import com.intellij.openapi.editor.colors.CodeInsightColors
import com.intellij.openapi.editor.colors.TextAttributesKey
import com.intellij.psi.PsiFile
import com.redhat.devtools.lsp4ij.features.semanticTokens.SemanticTokensColorsProvider

/**
 * Maps AUML semantic token types to editor colors, notably `unknown` (an unresolved element type) to the red
 * "unresolved reference" color.
 */
class AumlSemanticTokensColorsProvider : SemanticTokensColorsProvider {
    override fun getTextAttributesKey(tokenType: String, tokenModifiers: List<String>, file: PsiFile): TextAttributesKey? =
        when (tokenType) {
            "unknown" -> CodeInsightColors.WRONG_REFERENCES_ATTRIBUTES   // red: type not in any imported namespace
            "type" -> DefaultLanguageHighlighterColors.CLASS_NAME
            "namespace" -> DefaultLanguageHighlighterColors.CLASS_REFERENCE
            "property" -> DefaultLanguageHighlighterColors.INSTANCE_FIELD
            "macro" -> DefaultLanguageHighlighterColors.METADATA
            else -> null
        }
}
