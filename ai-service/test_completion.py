from datetime import date, timedelta
import math
import pytest
from app.main import _parse_vietnamese_amount_words, cashflow_forecast, ForecastRequest, DailyCashPoint, anomaly_detection, AnomalyRequest, AnomalyRecord

@pytest.mark.parametrize('words,expected', [('Hai mươi nghìn đồng',20000),('Hai trăm ba mươi lăm nghìn đồng chẵn',235000),('Một triệu hai trăm linh năm nghìn đồng',1205000),('Một tỷ hai trăm triệu đồng',1200000000),('Một nghìn tỷ đồng',1000000000000),('Công ty năm tháng',0)])
def test_amount_words(words,expected):
    assert _parse_vietnamese_amount_words(words)==expected

def test_forecast_reports_real_holdout_metrics():
    start=date(2026,7,1)
    rows=[DailyCashPoint(date=(start+timedelta(days=i)).isoformat(),inflow=1000+i*10,outflow=500+i*3) for i in range(40)]
    result=cashflow_forecast(ForecastRequest(history=rows,horizon=7))
    assert len(result['series'])==7
    assert result['quality']['holdoutDays']==8
    assert result['quality']['inflowMae']>=0
    assert result['quality']['inflowMae']<result['quality']['baselineInflowMae']
    assert math.isfinite(result['quality']['outflowMae'])

def test_duplicate_dates_do_not_fake_enough_history():
    rows=[DailyCashPoint(date='2026-07-01',inflow=1000,outflow=500) for _ in range(20)]
    assert cashflow_forecast(ForecastRequest(history=rows,horizon=7))['status']=='insufficient_data'

def test_anomaly_requires_minimum_records():
    assert anomaly_detection(AnomalyRequest(records=[AnomalyRecord(id='one',amount=100)]))['status']=='insufficient_data'

def test_nonfinite_amount_is_rejected():
    with pytest.raises(ValueError): AnomalyRecord(id='bad',amount=float('nan'))
