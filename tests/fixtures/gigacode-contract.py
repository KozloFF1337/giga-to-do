#!/usr/bin/env python3
"""Only a process/MCP contract fixture. Does not implement or test Gigacode."""
import argparse
import json
import pathlib
import sys
import time
import urllib.request
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('-p', required=True)
parser.add_argument('-o', required=True)
parser.add_argument('--approval-mode')
parser.add_argument('--max-wall-time')
parser.add_argument('--max-session-turns')
parser.add_argument('--max-tool-calls')
parser.add_argument('--model')
parser.add_argument('--resume')
args = parser.parse_args()
assert args.o == 'stream-json'
settings_path = pathlib.Path('.gigacode/settings.json')
settings = json.loads(settings_path.read_text())['mcpServers']['giga-todo']
session = args.resume or str(uuid.uuid4())
with pathlib.Path('contract-invocations.jsonl').open('a') as file:
    file.write(json.dumps({'session_id':session,'resumed':bool(args.resume),'format':args.o,'approval':args.approval_mode})+'\n')
print(json.dumps({'type':'system','session_id':session}), flush=True)

def rpc(method, parameters=None):
    payload = json.dumps({'jsonrpc':'2.0','id':1,'method':method,'params':parameters or {}}).encode()
    headers = dict(settings['headers'])
    headers.update({'Content-Type':'application/json','Accept':'application/json, text/event-stream'})
    request = urllib.request.Request(settings['httpUrl'], data=payload, headers=headers)
    with urllib.request.urlopen(request, timeout=10) as response:
        result = json.load(response)['result']
    if result.get('isError'):
        raise RuntimeError(result['content'][0]['text'])
    return result

rpc('initialize', {'protocolVersion':'2025-03-26','capabilities':{},'clientInfo':{'name':'giga-contract-fixture','version':'1'}})
rpc('tools/list')
context = json.loads(rpc('tools/call', {'name':'get_task','arguments':{}})['content'][0]['text'])
task = context['task']
if '[contract-attachments]' in task['prompt']:
    manifest = json.loads(pathlib.Path('attachments.json').read_text())
    assert manifest == context['attachments'] and len(manifest) == 1
    attachment = manifest[0]
    assert attachment['originalPath'] in args.p and attachment['contentPath'] in args.p
    assert pathlib.Path(attachment['originalPath']).is_file()
    assert (pathlib.Path(attachment['contentPath'])/'docs'/'requirements.txt').read_text() == 'attachment requirement'
if '[contract-fail]' in task['prompt']:
    print('CONTRACT FIXTURE deliberate failure', file=sys.stderr, flush=True)
    sys.exit(7)
if '[contract-wait]' in task['prompt']:
    time.sleep(120)
rpc('tools/call', {'name':'report_progress','arguments':{'text':'CONTRACT FIXTURE: проверка процесса и MCP, без реальной работы'}})
rpc('tools/call', {'name':'submit_result','arguments':{
    'outcome':'accepted' if task['stage']=='InReview' else 'completed',
    'summary':'CONTRACT FIXTURE ONLY. CLI Gigacode и внешние интеграции не проверены.',
    'checks':['Process launch, per-run configuration, JSON events, authenticated HTTP MCP'] + (['Attachment paths, manifest and extracted contents read by process'] if '[contract-attachments]' in task['prompt'] else [])
}})
