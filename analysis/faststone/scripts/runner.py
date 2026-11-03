import subprocess
import datetime
import os
import sys

script_directory = r"D:\TabTaskExplorer\analysis\faststone\scripts"
pending_script = os.path.join(script_directory, "pending-command.ps1")
log_path = r"D:\TabTaskExplorer\output\runner-last.txt"
exit_code_path = log_path + ".exitcode"
working_directory = r"D:\TabTaskExplorer"

lines = []
lines.append("开始时间=" + datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
lines.append("待执行脚本=" + pending_script)
lines.append("工作目录=" + working_directory)
lines.append("Python 版本=" + sys.version)

try:
    completed = subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", pending_script],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=working_directory,
        timeout=3600,
    )
    lines.append("退出码=" + str(completed.returncode))
    lines.append("--- 标准输出 ---")
    lines.append(completed.stdout if completed.stdout else "(空)")
    lines.append("--- 标准错误 ---")
    lines.append(completed.stderr if completed.stderr else "(空)")
    exit_code = completed.returncode
except subprocess.TimeoutExpired:
    lines.append("执行超时：超过 3600 秒")
    exit_code = 124
except Exception as error:
    lines.append("执行异常=" + str(error))
    exit_code = 1

lines.append("结束时间=" + datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))

with open(log_path, "w", encoding="utf-8") as log_file:
    log_file.write("\n".join(lines))

with open(exit_code_path, "w", encoding="utf-8") as exit_file:
    exit_file.write(str(exit_code))

print("执行结束 退出码=" + str(exit_code) + " 日志=" + log_path)
