---
name: receiving-code-review
description: Техническая оценка замечаний критика Giga To Do
---

# receiving-code-review — адаптация Superpowers для Giga To Do

Исходная методика закреплена на 22f0b463e7a5e579904dd6211a520321035526f7. Прочитай [инструкцию](../../vendor/superpowers/skills/receiving-code-review/SKILL.md) и её относительные reference-файлы. Следующие правила интеграции заменяют только механизмы диспетчеризации и коммуникации исходника:

- Для вопросов и ответов используй post_message и task-feedback. Не публикуй комментарии во внешний сервис без отдельного требования задачи.
- Процессы, очередь, worktree и отдельного критика предоставляет Giga To Do. Не вызывай subagent-driven-development, executing-plans, dispatching-parallel-agents, using-git-worktrees или finishing-a-development-branch.
- Исходные требования к доказательствам и согласованиям сохраняются. Вопросы человеку передавай через MCP, не ожидай интерактивного терминала.
- Для OpenSpec не создавай параллельный дизайн/план. Используй только релевантную часть методики и инструкции проекта.
- После submit_result заверши процесс.
