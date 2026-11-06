# Матрица навыков и агентов

Каталог 1.2.0. Источник: `src/GigaTodo/AgentConfig/catalog.json`. Генерация: `python3 scripts/export-agent-config.py`, проверка: та же команда с `--check`.

Внешние навыки не установлены на машине разработки; версии ожидаемые из пользовательского реестра. Матрица задаёт условный выбор, не запускает все навыки подряд. Состав MCP определяется окружением, ссылки не разбираются backend по сервисам.

## Профили

- **analytics-executor**: Аналитик; политика `executor`; сценарии: execute, feedback, decompose, generic-analysis, openspec-plan, openspec-design, context-proposals, archive, external-pr-feedback, pixso, debug, handoff, russian, analyst-chat, session-chat, skill-maintenance.
- **analytics-critic**: Критик: Аналитик; политика `critic`; сценарии: critique, openspec-design, pixso, debug, russian.
- **frontend-executor**: Frontend-разработчик; политика `executor`; сценарии: execute, feedback, decompose, openspec-plan, openspec-design, openspec-apply, context-proposals, archive, external-pr-feedback, frontend, pixso, tests, debug, handoff, russian, analyst-chat, session-chat, skill-maintenance.
- **frontend-critic**: Критик: Frontend-разработчик; политика `critic`; сценарии: critique, openspec-design, frontend, pixso, tests, debug, russian.
- **backend-executor**: Backend-разработчик; политика `executor`; сценарии: execute, feedback, decompose, openspec-plan, openspec-design, openspec-apply, context-proposals, archive, external-pr-feedback, pixso, tests, debug, handoff, russian, analyst-chat, session-chat, skill-maintenance.
- **backend-critic**: Критик: Backend-разработчик; политика `critic`; сценарии: critique, openspec-design, openspec-review, pixso, tests, debug, russian.
- **testing-executor**: Тестировщик; политика `executor`; сценарии: execute, feedback, decompose, openspec-plan, openspec-design, context-proposals, archive, external-pr-feedback, frontend, pixso, tests, debug, handoff, russian, analyst-chat, session-chat, skill-maintenance.
- **testing-critic**: Критик: Тестировщик; политика `critic`; сценарии: critique, openspec-design, frontend, pixso, tests, debug, russian.

Ответ исполнителя использует отдельную политику `reply`, сценарии feedback/russian и прежнюю executor-сессию. Он не запускает реализацию.

## Сценарии

### execute
Каждый исполнитель начинает задачу
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: task-execution.
- По назначению: —.
- Условия: Прочитать и соблюдать полный исходник навыка.

### critique
Каждый ход критика
- Роли: Critic; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: task-critique → verification-before-completion.
- По назначению: —.
- Условия: Прочитать и соблюдать полный исходник навыка.

### feedback
Ответ на вопрос или замечания критика
- Роли: Executor, ExecutorReply; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: task-feedback → receiving-code-review.
- По назначению: —.
- Условия: Прочитать и соблюдать полный исходник навыка.

### decompose
Поручена декомпозиция или создание карточек из источника
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: task-decomposition.
- По назначению: —.
- Условия: Прочитать и соблюдать полный исходник навыка.

### generic-analysis
Нет OpenSpec, нужно проработать требования
- Роли: Executor; доски: analytics; процесс: generic.
- Обязательно по порядку: brainstorming → writing-plans.
- По назначению: —.
- Условия: Прочитать и соблюдать полный исходник навыка.

### openspec-plan
В проекте OpenSpec и требуется новый план
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: openspec.
- Обязательно по порядку: openspec-context.
- По назначению: openspec-backend-sdd, openspec-jira-plan-full, openspec-jira-plan.
- Условия: Выбрать ровно один полный вход; openspec-jira-plan лишь указатель. Остановить полный цикл на границе текущего задания; реализацию, критику и архив не выполнять самовольно.

### openspec-design
Нужна проверка proposal/specs и дизайна
- Роли: Executor, Critic; доски: analytics, frontend, backend, testing; процесс: openspec.
- Обязательно по порядку: openspec-context → openspec-artifacts-check.
- По назначению: openspec-artifacts-roast.
- Условия: Roast только после наличия всех пяти артефактов. Человеческий design-grill не заменять решением агента.

### openspec-apply
Есть утверждённые артефакты и требуется реализация
- Роли: Executor; доски: backend, frontend; процесс: openspec.
- Обязательно по порядку: openspec-context → openspec-apply.
- По назначению: test-runner.
- Условия: Не начинать код до закрытия необходимых гейтов.

### openspec-review
Проверяется совместимый OpenSpec backend
- Роли: Critic; доски: backend; процесс: openspec.
- Обязательно по порядку: openspec-context → ksupbim-review.
- По назначению: openspec-artifacts-roast.
- Условия: Проверить предположения о сабмодулях и структуре проекта; иначе общий критик.

### context-proposals
Явно поручено извлечение контекста из диффа
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: openspec.
- Обязательно по порядку: openspec-context → openspec-context-extract.
- По назначению: —.
- Условия: В постоянный контекст писать только после предусмотренного одобрения.

### archive
Явно поручена архивация принятого и смерженного изменения
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: openspec.
- Обязательно по порядку: openspec-context → openspec-archive → openspec-archive-cli.
- По назначению: —.
- Условия: Все условия исходника: tasks закрыты, grill закрыт, тесты зелёные, PR/MR слит. Done не запускает архив.

### external-pr-feedback
Поручен разбор замечаний внешнего PR
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: pr-plan.
- По назначению: task-feedback.
- Условия: Только если skill поддерживает источник. Для другого хостинга общий feedback, без требований конкретного MCP.

### frontend
Нужна реализация или проверка интерфейса
- Роли: Executor, Critic; доски: frontend, testing; процесс: any.
- Обязательно по порядку: frontend-development.
- По назначению: pixso-design-reader.
- Условия: Критик не изменяет исходники. Получить browser lease до devtools.

### pixso
В задании есть макет Pixso
- Роли: Executor, Critic; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: pixso-design-reader.
- По назначению: —.
- Условия: Требуются pixso-mockup-reader и devtools. Нет помощника — блокировка. Скриншоты в файлы; не resize/emulate на Pixso.

### tests
Поручены тестирование или реализация проверяемого поведения
- Роли: Executor, Critic; доски: backend, frontend, testing; процесс: any.
- Обязательно по порядку: verification-before-completion.
- По назначению: test-runner, test-driven-development.
- Условия: test-runner применять при совместимости проекта; критик не дописывает тесты в исходники. TDD — для изменения поведения, а не чтения.

### debug
Обнаружен дефект или падение проверки
- Роли: Executor, Critic; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: systematic-debugging.
- По назначению: test-driven-development.
- Условия: Прочитать и соблюдать полный исходник навыка.

### handoff
Исполнитель передаёт результат критику
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: verification-before-completion → requesting-code-review.
- По назначению: —.
- Условия: Передать пакет через submit_result; не запускать собственного reviewer.

### russian
Подготовка русского текста
- Роли: Executor, Critic, ExecutorReply; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: ru-text.
- По назначению: ru-check, ru-score.
- Условия: ru-check — при порученной вычитке, ru-score — при запросе оценки; не менять исходный файл без поручения.

### analyst-chat
Явно поручены вопросы внешним аналитикам
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: sberchat-analyst.
- По назначению: —.
- Условия: Согласовать внешние сообщения по исходнику; только разрешённая группа и открытый пункт. Иначе request_clarification.

### session-chat
Явно включено ведение сессии через внешний чат
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: gigacode-chat.
- По назначению: —.
- Условия: Не включать статусное дублирование по умолчанию. Соблюдать дословные строки и подтверждения из полного SKILL.md.

### skill-maintenance
Явно поручено создание/изменение навыка
- Роли: Executor; доски: analytics, frontend, backend, testing; процесс: any.
- Обязательно по порядку: skill-builder.
- По назначению: —.
- Условия: Прочитать полный канон; исходник требует три файла и самопроверку. Не изменять пользовательские навыки автоматически.

## Реестр навыков

| Навык | Версия | Источник |
|---|---|---|
| `openspec-context` | 2.2.0 | user |
| `openspec-backend-sdd` | 2.4.0 | user |
| `openspec-jira-plan-full` | 2.4.0 | user |
| `openspec-jira-plan` | 4.3.0 | user |
| `openspec-apply` | 2.3.0 | user |
| `openspec-artifacts-check` | 1.2.0 | user |
| `openspec-artifacts-roast` | 1.5.0 | user |
| `openspec-context-extract` | 1.4.0 | user |
| `openspec-archive` | 2.4.0 | user |
| `openspec-archive-cli` | 1.4.0 | user |
| `ksupbim-review` | 3.4.1 | user |
| `pr-plan` | 5.4.0 | user |
| `test-runner` | 3.5.0 | user |
| `sberchat-analyst` | 1.4.0 | user |
| `gigacode-chat` | 1.3.0 | user |
| `pixso-design-reader` | 1.0.0 | user |
| `ru-text` | 2.10.10+g1 | user |
| `ru-check` | 2.10.10+g1 | user |
| `ru-score` | 2.10.10+g1 | user |
| `skill-builder` | 1.1.0 | user |
| `brainstorming` | 1.0.0 | superpowers-adapter |
| `frontend-development` | 1.0.0 | application |
| `receiving-code-review` | 1.0.0 | superpowers-adapter |
| `requesting-code-review` | 1.0.0 | superpowers-adapter |
| `systematic-debugging` | 1.0.0 | superpowers-adapter |
| `task-critique` | 1.0.0 | application |
| `task-decomposition` | 1.0.0 | application |
| `task-execution` | 1.0.0 | application |
| `task-feedback` | 1.0.0 | application |
| `test-driven-development` | 1.0.0 | superpowers-adapter |
| `verification-before-completion` | 1.0.0 | superpowers-adapter |
| `writing-plans` | 1.0.0 | superpowers-adapter |

## Разрешения

Шаблоны permissions записываются в локальный settings.json запуска вместе с MCP приложения. Для Critic/ExecutorReply запуск принудительно использует default approval mode. Запрещены штатные инструменты записи и изменяющие Git-команды, произвольные интерпретаторы требуют подтверждения. Это не OS sandbox: точные имена инструментов и действия внешних MCP нужно подтвердить в целевой версии. Не считать шаблон технически проверенным ограничением чтения.

Все права CLI и сервера сильнее инструкций. Подтверждения из исходных навыков не снимаются. Не добавлять trust=true всем MCP. Инструкции сценариев не являются разрешением внешних действий.

Шаблоны native agents не запускаются дополнительно к main-процессу. Внешний pixso-mockup-reader не пересоздаётся по неполному реестру; его отсутствие фиксируется как блокировка.
