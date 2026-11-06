---
name: systematic-debugging
description: Систематическая диагностика ошибок в задаче Giga To Do
---

# systematic-debugging — адаптация Superpowers для Giga To Do

Исходная методика закреплена на 22f0b463e7a5e579904dd6211a520321035526f7. Прочитай [инструкцию](../../vendor/superpowers/skills/systematic-debugging/SKILL.md) и её относительные reference-файлы. Следующие правила интеграции заменяют только механизмы диспетчеризации и коммуникации исходника:

- Диагностика не меняет очередь. Если требуется человек, request_clarification. Упоминания superpowers:test-driven-development и verification-before-completion означают одноимённые навыки этого расширения.
- Процессы, очередь, worktree и отдельного критика предоставляет Giga To Do. Не вызывай subagent-driven-development, executing-plans, dispatching-parallel-agents, using-git-worktrees или finishing-a-development-branch.
- Исходные требования к доказательствам и согласованиям сохраняются. Вопросы человеку передавай через MCP, не ожидай интерактивного терминала.
- Для OpenSpec не создавай параллельный дизайн/план. Используй только релевантную часть методики и инструкции проекта.
- После submit_result заверши процесс.
