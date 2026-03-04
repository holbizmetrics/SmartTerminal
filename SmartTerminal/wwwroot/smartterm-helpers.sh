#!/bin/bash
# SmartTerminal Rich Rendering Helpers
# Source this file: . smartterm-helpers.sh
#
# LaTeX:
#   latex '\frac{a}{b}'                 # inline
#   latex -d '\int_0^\infty e^{-x} dx'  # display mode
#   latex -b '\begin{matrix} a \\ b \end{matrix}'  # block mode
#
# Markdown:
#   md '# Hello **world**'              # plain Markdown
#   md -l '# Result: $E = mc^2$'        # Markdown with LaTeX math
#   mdfile README.md                     # render a Markdown file
#   mdfile -l notes.md                   # render file with LaTeX math

latex() {
    local mode="inline"
    local src=""

    while [ $# -gt 0 ]; do
        case "$1" in
            -d|--display) mode="display"; shift ;;
            -b|--block)   mode="block"; shift ;;
            -i|--inline)  mode="inline"; shift ;;
            *)            src="$1"; shift ;;
        esac
    done

    if [ -z "$src" ]; then
        echo "Usage: latex [-d|-b|-i] 'LATEX_EXPRESSION'" >&2
        return 1
    fi

    local encoded
    encoded=$(printf '%s' "$src" | base64 | tr -d '\n')
    printf '\033]1337;latex=%s;mode=%s\007' "$encoded" "$mode"
}

md() {
    local enable_latex=""
    local src=""

    while [ $# -gt 0 ]; do
        case "$1" in
            -l|--latex) enable_latex=";latex=1"; shift ;;
            *)          src="$1"; shift ;;
        esac
    done

    if [ -z "$src" ]; then
        echo "Usage: md [-l] 'MARKDOWN_TEXT'" >&2
        return 1
    fi

    local encoded
    encoded=$(printf '%s' "$src" | base64 | tr -d '\n')
    printf '\033]1337;markdown=%s%s\007' "$encoded" "$enable_latex"
}

mdfile() {
    local enable_latex=""
    local file=""

    while [ $# -gt 0 ]; do
        case "$1" in
            -l|--latex) enable_latex=";latex=1"; shift ;;
            *)          file="$1"; shift ;;
        esac
    done

    if [ -z "$file" ] || [ ! -f "$file" ]; then
        echo "Usage: mdfile [-l] FILE" >&2
        return 1
    fi

    local encoded
    encoded=$(base64 < "$file" | tr -d '\n')
    printf '\033]1337;markdown=%s%s\007' "$encoded" "$enable_latex"
}
