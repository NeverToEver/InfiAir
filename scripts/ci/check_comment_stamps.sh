#!/usr/bin/env bash
# Comment date-stamp gate.
# Usage: check_comment_stamps.sh
# 扫描源文件注释里的 2026-0x-xx 日期戳（AGENTS.md「注释禁日期戳与审计轮次编号」）。
# 只判注释行：日期需位于注释标记（// 或 #）之后才计命中，
# 代码字面量（如 random.seed 数字）不受影响。
# docs/** 与 *.md 不扫描——文档里的日期是合法时间线锚点。
set -uo pipefail

hit=0
# git grep 只枚举已跟踪文件，天然排除 .godot/、builds/、vendor 等产物
while IFS= read -r line; do
  [ -z "$line" ] && continue
  file="${line%%:*}"
  rest="${line#*:}"
  slash_idx=$(awk '{ i = index($0, "//"); print (i == 0 ? 999999 : i) }' <<<"$rest")
  hash_idx=$(awk '{ i = index($0, "#"); print (i == 0 ? 999999 : i) }' <<<"$rest")
  cidx=$slash_idx
  [ "$hash_idx" -lt "$cidx" ] && cidx=$hash_idx
  didx=$(awk '{ if (match($0, /20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]/)) print RSTART; else print 999999 }' <<<"$rest")
  if [ "$didx" -ne 999999 ] && [ "$didx" -gt "$cidx" ]; then
    echo "$file:$rest" >&2
    hit=$((hit + 1))
  fi
done < <(git grep -nE '20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]' -- ':!docs' ':!*.md' 2>/dev/null)

if [ "$hit" -gt 0 ]; then
  echo "::error::注释含日期戳 $hit 处（AGENTS.md：注释禁日期戳）" >&2
  exit 1
fi
echo "comment-stamp gate: clean"
