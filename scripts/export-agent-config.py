#!/usr/bin/env python3
"""Export versioned agent templates and documentation. Never calls Gigacode."""
import argparse
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true', help='Compare generated files, without modifying them')
args = parser.parse_args()
catalog = json.loads((root/'src/GigaTodo/AgentConfig/catalog.json').read_text())
scenarios = {s['id']:s for s in catalog['scenarios']}
policies = {p['id']:p for p in catalog['policies']}
outputs = {}
for profile in catalog['profiles']:
    policy = policies[profile['policyId']]
    lines = ['---', 'name: '+profile['id'], 'description: '+profile['title']+' для назначенной задачи Giga To Do; выбирается диспетчером, не для автоматического делегирования.', 'model: inherit', 'approvalMode: default']
    if policy['disallowedTools']:
        lines += ['disallowedTools:']+['  - '+t for t in policy['disallowedTools']]
    lines += ['---','','# '+profile['title'],'',f"Профиль `{profile['id']}`, каталог {catalog['version']}. Основной исполнитель — отдельная main-сессия, которую запускает C#-сервис. Этот named-agent файл — совместимый шаблон, не команда создавать ещё одного исполнителя.",'',policy['instructions'],'','Выбирай сценарии по фактической цели, а не все подряд. Необходимые навыки сначала обнаружить и прочитать. Для OpenSpec openspec-context первым; не запускать оба полных входа. Отсутствующий обязательный навык — блокировка, а не выдуманный результат.','']
    for sid in profile['scenarioIds']:
        s = scenarios[sid]
        lines += ['## '+sid, s['when'], 'Порядок: '+ ' → '.join(s['required']), 'Дополнительно: '+(', '.join(s['optional']) or '—'), 'Гейт: '+(s['gate'] or 'Применять по инструкции навыка.'),'']
    lines += ['Итог через submit_result; после передачи завершить процесс. Общение через MCP приложения. Нельзя делать merge/архив как побочный эффект принятия карточки. Не обходить подтверждения пользовательских навыков.','']
    outputs[root/'extensions/giga-todo/agents'/(profile['id']+'.md')] = '\n'.join(lines)
for policy in catalog['policies']:
    outputs[root/'extensions/giga-todo/permissions'/(policy['id']+'.settings.json')] = json.dumps({'permissions':{k:policy[k] for k in ['allow','ask','deny']}},ensure_ascii=False,indent=2)+'\n'
outputs[root/'extensions/giga-todo/agent-catalog.json'] = json.dumps(catalog,ensure_ascii=False,indent=2)+'\n'
lines=['# Матрица навыков и агентов','',f"Каталог {catalog['version']}. Источник: `src/GigaTodo/AgentConfig/catalog.json`. Генерация: `python3 scripts/export-agent-config.py`, проверка: та же команда с `--check`.",'','Внешние навыки не установлены на машине разработки; версии ожидаемые из пользовательского реестра. Матрица задаёт условный выбор, не запускает все навыки подряд. Состав MCP определяется окружением, ссылки не разбираются backend по сервисам.','','## Профили','']
for p in catalog['profiles']:
    lines += [f"- **{p['id']}**: {p['title']}; политика `{p['policyId']}`; сценарии: "+', '.join(p['scenarioIds'])+'.']
lines += ['','Ответ исполнителя использует отдельную политику `reply`, сценарии feedback/russian и прежнюю executor-сессию. Он не запускает реализацию.','','## Сценарии','']
for s in catalog['scenarios']:
    lines += ['### '+s['id'],s['when'], '- Роли: '+', '.join(s['roles'])+'; доски: '+', '.join(s['boards'])+'; процесс: '+s['workflow']+'.','- Обязательно по порядку: '+' → '.join(s['required'])+'.','- По назначению: '+(', '.join(s['optional']) or '—')+'.','- Условия: '+(s['gate'] or 'Прочитать и соблюдать полный исходник навыка.') ,'']
lines += ['## Реестр навыков','','| Навык | Версия | Источник |','|---|---|---|']
lines += [f"| `{s['id']}` | {s['version']} | {s['origin']} |" for s in catalog['skills']]
lines += ['','## Разрешения','','Шаблоны permissions записываются в локальный settings.json запуска вместе с MCP приложения. Для Critic/ExecutorReply запуск принудительно использует default approval mode. Запрещены штатные инструменты записи и изменяющие Git-команды, произвольные интерпретаторы требуют подтверждения. Это не OS sandbox: точные имена инструментов и действия внешних MCP нужно подтвердить в целевой версии. Не считать шаблон технически проверенным ограничением чтения.','', 'Все права CLI и сервера сильнее инструкций. Подтверждения из исходных навыков не снимаются. Не добавлять trust=true всем MCP. Инструкции сценариев не являются разрешением внешних действий.','','Шаблоны native agents не запускаются дополнительно к main-процессу. Внешний pixso-mockup-reader не пересоздаётся по неполному реестру; его отсутствие фиксируется как блокировка.','']
outputs[root/'docs/SKILL_MATRIX.md'] = '\n'.join(lines)
for path,text in outputs.items():
    if args.check:
        if not path.exists() or path.read_text()!=text: raise SystemExit('Generated file out of date: '+str(path.relative_to(root)))
    else:
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(text)
print(('Checked' if args.check else 'Exported'),len(outputs),'files; no Gigacode executed.')
