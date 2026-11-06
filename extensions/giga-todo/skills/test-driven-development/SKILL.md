---
name: test-driven-development
description: Разработка проверяемого поведения через failing test и минимальную реализацию
---

# test-driven-development — адаптация Superpowers для Giga To Do

Исходная методика закреплена на 22f0b463e7a5e579904dd6211a520321035526f7. Прочитай [инструкцию](../../vendor/superpowers/skills/test-driven-development/SKILL.md) и её относительные reference-файлы. Следующие правила интеграции заменяют только механизмы диспетчеризации и коммуникации исходника:

- Применяй в задачах разработки проверяемого поведения. Уточнения и исключения по инструкции — через request_clarification. Не запускай дополнительные исполнители.
- Процессы, очередь, worktree и отдельного критика предоставляет Giga To Do. Не вызывай subagent-driven-development, executing-plans, dispatching-parallel-agents, using-git-worktrees или finishing-a-development-branch.
- Исходные требования к доказательствам и согласованиям сохраняются. Вопросы человеку передавай через MCP, не ожидай интерактивного терминала.
- Для OpenSpec не создавай параллельный дизайн/план. Используй только релевантную часть методики и инструкции проекта.
- После submit_result заверши процесс.
