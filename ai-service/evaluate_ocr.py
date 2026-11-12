"""Offline evaluation of paired OCR output and human-corrected labels. No training or accuracy claim without labels."""
import argparse
import json
import unicodedata
from pathlib import Path
FIELDS = ('supplierName','supplierTaxCode','invoiceNumber','serial','invoiceDate','subtotal','vatAmount','total')
MONEY = {'subtotal','vatAmount','total'}
def normalized(value):
    return ' '.join(unicodedata.normalize('NFKC', str(value or '')).casefold().split())
def camel(record):
    return {key[:1].lower()+key[1:]:value for key,value in record.items()}
def evaluate(path):
    scores={field:{'correct':0,'labeled':0} for field in FIELDS}
    samples=0
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        if not line.strip():continue
        row=json.loads(line)
        original=row.get('original',row.get('prediction',{}))
        expected=row.get('corrected',row.get('expected',{}))
        if isinstance(original,str):original=json.loads(original)
        if isinstance(expected,str):expected=json.loads(expected)
        original,expected=camel(original),camel(expected)
        samples+=1
        for field in FIELDS:
            if field not in expected or expected[field] in (None,''):continue
            scores[field]['labeled']+=1
            if field in MONEY:
                try:ok=abs(float(original.get(field,0))-float(expected[field]))<=1
                except (ValueError,TypeError):ok=False
            else:ok=normalized(original.get(field))==normalized(expected[field])
            scores[field]['correct']+=int(ok)
    for result in scores.values():result['accuracy']=result['correct']/result['labeled'] if result['labeled'] else None
    return {'samples':samples,'perField':scores,'note':'Paired-label field accuracy on the supplied dataset only; not a legal or generalization guarantee.'}
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('dataset');parser.add_argument('--output');args=parser.parse_args()
    result=json.dumps(evaluate(args.dataset),ensure_ascii=False,indent=2)
    if args.output:Path(args.output).write_text(result,encoding='utf-8')
    print(result)
