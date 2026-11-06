---
name: requesting-code-review
description: Подготовка результата для отдельного критика Giga To Do
---

# requesting-code-review — адаптация Superpowers для Giga To Do

Исходная методика закреплена на 22f0b463e7a5e579904dd6211a520321035526f7. Прочитай [инструкцию](../../vendor/superpowers/skills/requesting-code-review/SKILL.md) и её относительные reference-файлы. Следующие правила интеграции заменяют только механизмы диспетчеризации и коммуникации исходника:

- Не запускай reviewer subagent. Подготовь пакет проверки по code-reviewer.md, затем submit_result outcome=completed: отдельного критика запустит сервис.
- Процессы, очередь, worktree и отдельного критика предоставляет Giga To Do. Не вызывай subagent-driven-development, executing-plans, dispatching-parallel-agents, using-git-worktrees или finishing-a-development-branch.
- Исходные требования к доказательствам и согласованиям сохраняются. Вопросы человеку передавай через MCP, не ожидай интерактивного терминала.
- Для OpenSpec не создавай параллельный дизайн/план. Используй только релевантную часть методики и инструкции проекта.
- После submit_result заверши процесс.
