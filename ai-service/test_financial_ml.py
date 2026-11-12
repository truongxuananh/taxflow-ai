from datetime import date, timedelta
import importlib.util
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location('taxflow_ai_main', Path(__file__).parent / 'app' / 'main.py')
main = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = main
assert spec.loader is not None
spec.loader.exec_module(main)


def test_isolation_forest_flags_extreme_invoice():
    rows = [main.AnomalyRecord(id=str(i), amount=1_000_000 + i * 20_000, vatRate=10,
            supplierFrequency=6, amountVsSupplierAverage=1, daysSinceLastInvoice=14, invoiceFrequency=2)
            for i in range(15)]
    rows.append(main.AnomalyRecord(id='extreme', amount=35_000_000, vatRate=17,
                supplierFrequency=0, amountVsSupplierAverage=12, daysSinceLastInvoice=365, invoiceFrequency=0))
    result = main.anomaly_detection(main.AnomalyRequest(records=rows, contamination=0.08))
    extreme = next(x for x in result['results'] if x['id'] == 'extreme')
    assert result['method'] == 'IsolationForest'
    assert extreme['riskScore'] >= 70
    assert extreme['isAnomaly']


def test_cashflow_forecast_returns_requested_horizon():
    start = date(2026, 7, 1)
    history = [main.DailyCashPoint(date=(start + timedelta(days=i)).isoformat(),
               inflow=1_000_000 + i * 15_000, outflow=700_000 + i * 5_000) for i in range(45)]
    result = main.cashflow_forecast(main.ForecastRequest(history=history, horizon=30))
    assert result['status'] == 'ok'
    assert len(result['series']) == 30
    assert 'HuberRegressor' in result['method']


if __name__ == '__main__':
    test_isolation_forest_flags_extreme_invoice()
    test_cashflow_forecast_returns_requested_horizon()
    print('financial ML tests: PASS')
