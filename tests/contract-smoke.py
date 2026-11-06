"""HTTP smoke test for an app explicitly started with gigacode-contract.py."""
import json
import io
import zipfile
import os
import time
import urllib.error
import urllib.request

base = os.environ.get('GIGA_TEST_URL', 'http://localhost:5180')

def api(path, method='GET', data=None):
    request = urllib.request.Request(base+'/api/'+path, data=None if data is None else json.dumps(data).encode(), method=method, headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(request, timeout=10) as response:
        content = response.read()
        return json.loads(content) if content else None

def wait_task(task_id, predicate):
    for _ in range(80):
        detail = api('tasks/'+task_id)
        if predicate(detail):
            return detail
        time.sleep(.25)
    raise AssertionError('Task did not reach expected state: '+json.dumps(detail))

stamp = str(time.time_ns())
runtime = api('runtime')
assert runtime['executable'].endswith('gigacode-contract.py'), 'Run only with the explicit contract fixture executable'
try:
    api('runtime','PUT',{'mode':'Gigacode'})
    task = api('tasks','POST',{'boardId':'analytics','prompt':'Contract success '+stamp})['task']
    detail = wait_task(task['id'], lambda d:d['task']['stage']=='Done')
    assert 'CONTRACT FIXTURE' in detail['task']['resultJson']
    assert detail['task']['executorSession'] and detail['task']['criticSession']
    assert len(detail['runs']) == 2
    assert all(r['state']=='Succeeded' for r in detail['runs'])
    archive = io.BytesIO()
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as file:
        file.writestr('docs/requirements.txt', 'attachment requirement')
    boundary = 'giga-contract-'+stamp
    multipart = ('--'+boundary+'\r\nContent-Disposition: form-data; name="files"; filename="input.zip"\r\nContent-Type: application/zip\r\n\r\n').encode()+archive.getvalue()+('\r\n--'+boundary+'--\r\n').encode()
    request = urllib.request.Request(base+'/api/attachments', data=multipart, method='POST', headers={'Content-Type':'multipart/form-data; boundary='+boundary})
    with urllib.request.urlopen(request, timeout=10) as response:
        attachment = json.load(response)['attachments'][0]
    assert attachment['state']=='Extracted'
    attached = api('tasks','POST',{'boardId':'analytics','prompt':'[contract-attachments] '+stamp,'attachmentIds':[attachment['id']]})['task']
    attached_result = wait_task(attached['id'], lambda d:d['task']['stage']=='Done')
    assert 'Attachment paths, manifest and extracted contents' in attached_result['task']['resultJson']
    failed = api('tasks','POST',{'boardId':'backend','prompt':'[contract-fail] '+stamp})['task']
    failure = wait_task(failed['id'], lambda d:d['task']['blocker'] is not None)
    assert failure['task']['stage']=='InWork' and failure['runs'][0]['state']=='Failed'
    api('tasks/'+failed['id']+'/resume','POST',{'version':failure['task']['version']})
    failure2 = wait_task(failed['id'], lambda d:len(d['runs'])==2 and d['task']['blocker'] is not None)
    assert failure2['task']['executorSession']==failure['task']['executorSession']
    waiting = api('tasks','POST',{'boardId':'testing','prompt':'[contract-wait] '+stamp})['task']
    running = wait_task(waiting['id'], lambda d:d['task']['activeRunId'] is not None and d['task']['executorSession'] is not None)
    api('tasks/'+waiting['id']+'/cancel','POST',{'version':running['task']['version']})
    cancelled = wait_task(waiting['id'], lambda d:d['task']['activeRunId'] is None)
    assert cancelled['runs'][0]['state']=='Cancelled'
    try:
        request=urllib.request.Request(base+'/mcp',data=b'{"jsonrpc":"2.0","id":1,"method":"tools/list"}',headers={'Content-Type':'application/json'})
        urllib.request.urlopen(request)
        raise AssertionError('Unauthenticated MCP accepted')
    except urllib.error.HTTPError as error:
        assert error.code == 401
    try:
        request=urllib.request.Request(base+'/api/runtime',data=b'{"mode":"Mock"}',method='PUT',headers={'Content-Type':'application/json','Origin':'https://untrusted.example'})
        urllib.request.urlopen(request)
        raise AssertionError('Foreign Origin accepted')
    except urllib.error.HTTPError as error:
        assert error.code == 403
    print('PASS: process fixture -> HTTP MCP -> executor/critic Done, attachment manifest/paths/extracted contents, session IDs, nonzero exit, resume, cancellation, auth and Origin rejection. Real Gigacode not tested.')
finally:
    api('runtime','PUT',{'mode':'Disabled'})
