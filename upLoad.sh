#!/bin/bash
# TabTaskExplorer 快捷上传：提交全部变更并推送到 Gitee（origin main）
# 用法：在仓库根目录执行  bash upLoad.sh

git pull

python convert_to_utf8.py --strip-bom

git add --all

# 没有暂存变更时直接结束，避免产生空提交
if git diff --cached --quiet; then
    echo "没有需要提交的变更"
else
    git commit -m "快捷上传最新代码"
fi

git push origin main
