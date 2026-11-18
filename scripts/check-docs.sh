#!/usr/bin/env bash
# Предупреждения (никогда не ошибка):
#   1) код изменился, а docs/architecture.md и CHANGELOG.md — нет;
#   2) ссылка на код [путь:строки](путь#Lстроки) битая или текст не совпадает с якорем;
#   3) обычная относительная markdown-ссылка ведёт на несуществующий путь;
#   4) голая запись «путь:строка» вне markdown-ссылки.
# Использование:
#   bash scripts/check-docs.sh                      # pre-commit: git diff --cached
#   bash scripts/check-docs.sh <base-ref>           # CI: git diff <base-ref> HEAD
#   bash scripts/check-docs.sh "" <файл-документа>  # только проверки ссылок в одном файле
# Всегда завершается с кодом 0. Запускать из корня репозитория.
# Все пути в markdown-ссылках разрешаются от каталога документа.
set -u

base="${1:-}"
doc_override="${2:-}"

# --- 1. Код изменился, документация — нет ---
if [ -z "$doc_override" ]; then
    if [ -n "$base" ]; then
        changed="$(git diff --name-only "$base" HEAD 2>/dev/null || true)"
    elif ! git diff --cached --quiet 2>/dev/null; then
        changed="$(git diff --cached --name-only 2>/dev/null || true)"
    else
        changed="$(git diff --name-only HEAD^ HEAD 2>/dev/null || true)"
    fi

    code_changed=0
    docs_changed=0

    while IFS= read -r file; do
        [ -z "$file" ] && continue
        case "$file" in
            src/*|Program.cs) code_changed=1 ;;
            docs/architecture.md|CHANGELOG.md) docs_changed=1 ;;
        esac
    done <<< "$changed"

    if [ "$code_changed" -eq 1 ] && [ "$docs_changed" -eq 0 ]; then
        echo "WARNING: изменён код (src/ или Program.cs), но docs/architecture.md и CHANGELOG.md не обновлены."
        echo "WARNING: обновите документацию в этом же коммите (AGENTS.md, раздел «Документация»)."
    fi
fi

# --- Список документов ---
doc_files=()
if [ -n "$doc_override" ]; then
    doc_files+=("$doc_override")
else
    for candidate in README.md CHANGELOG.md AGENTS.md; do
        [ -f "$candidate" ] && doc_files+=("$candidate")
    done
    while IFS= read -r candidate; do
        doc_files+=("$candidate")
    done < <(find docs -type f -name '*.md' 2>/dev/null | sort)
fi

if [ "${#doc_files[@]}" -eq 0 ]; then
    exit 0
fi

# --- 2-4. Проверки ссылок ---
for doc in "${doc_files[@]}"; do
    doc_dir="$(dirname "$doc")"
    constructs="$(grep -oE '\[[^]]+\]\([^)]+\)' "$doc" 2>/dev/null || true)"

    while IFS= read -r construct; do
        [ -z "$construct" ] && continue
        label="${construct#\[}"
        label="${label%%](*}"
        target="${construct##*](}"
        target="${target%)}"

        case "$target" in
            http://*|https://*|mailto:*|tel:*|'#'*|/*) continue ;;
        esac

        case "$target" in
            *'#L'[0-9]*)
                # --- 2. Ссылка на код с якорем ---
                target_path="${target%%#*}"
                anchor="${target##*#}"
                anchor_first="${anchor#L}"
                anchor_last=""
                case "$anchor_first" in
                    *-L*) anchor_last="${anchor_first##*-L}"; anchor_first="${anchor_first%%-L*}" ;;
                esac

                if [ ! -f "$doc_dir/$target_path" ]; then
                    echo "WARNING: $doc — [$label]($target): файл не найден"
                    continue
                fi

                first=$((10#${anchor_first}))
                if [ -n "$anchor_last" ]; then
                    last=$((10#${anchor_last}))
                else
                    last="$first"
                fi
                total="$(awk 'END { print NR }' "$doc_dir/$target_path")"

                if [ "$first" -gt "$last" ] || [ "$last" -gt "$total" ]; then
                    echo "WARNING: $doc — [$label]($target): строки вне файла (всего $total)"
                    continue
                fi
                if [ -z "$(sed -n "${first}p" "$doc_dir/$target_path" | tr -d '[:space:]')" ]; then
                    echo "WARNING: $doc — [$label]($target): строка $first пустая"
                fi

                case "$label" in
                    *:*)
                        label_path="${label%%:*}"
                        label_lines="${label##*:}"
                        if [[ "$label_lines" =~ ^[0-9]+(-[0-9]+)?$ ]]; then
                            label_first="${label_lines%%-*}"
                            label_last=""
                            case "$label_lines" in
                                *-*) label_last="${label_lines##*-}" ;;
                            esac
                            if [ "$((10#${label_first}))" -ne "$first" ] \
                                || { [ -n "$label_last" ] && [ "$((10#${label_last}))" -ne "$last" ]; }; then
                                echo "WARNING: $doc — [$label]($target): текст не совпадает с якорем"
                            fi
                            if [ "$(basename "$label_path")" != "$(basename "$target_path")" ]; then
                                echo "WARNING: $doc — [$label]($target): имя файла в тексте и в ссылке различается"
                            fi
                        fi
                        ;;
                esac
                ;;
            *)
                # --- 3. Обычная markdown-ссылка ---
                plain="${target%%#*}"
                [ -z "$plain" ] && continue
                if [ ! -e "$doc_dir/$plain" ]; then
                    echo "WARNING: $doc — [$label]($target): путь не найден"
                fi
                ;;
        esac
    done <<< "$constructs"

    # --- 4. Голые записи пути со строками вне ссылок ---
    bare="$(sed -E 's/\[[^]]*\]\([^)]*\)//g' "$doc" 2>/dev/null \
        | grep -oE '[A-Za-z0-9_][A-Za-z0-9_./-]*\.(cs|json|yml|yaml|md|sh|props|slnx|csproj):[0-9]+(-[0-9]+)?' \
        | sort -u || true)"

    while IFS= read -r ref; do
        [ -z "$ref" ] && continue
        echo "WARNING: $doc — голая запись $ref: оформите ссылкой на код с якорем #L"
    done <<< "$bare"
done

exit 0
