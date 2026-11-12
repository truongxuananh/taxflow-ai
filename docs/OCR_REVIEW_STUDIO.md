# OCR Review Studio & Hybrid OCR

TaxFlow-AI now supports an explainable OCR workflow:

1. `ai-service` runs local/offline OCR with Tesseract Vietnamese + English.
2. `AI_PROVIDER=hybrid` calls local OCR first.
3. Gemini is only used when critical fields are missing, arithmetic is inconsistent, or local confidence is below `HYBRID_CONFIDENCE_THRESHOLD`.
4. The backend validates OCR output and stores the original document unchanged.
5. The Review Studio shows OCR evidence boxes on JPG/PNG, lets the reviewer focus a field, drag a region and run region-level OCR.
6. Confirmation writes the reviewed invoice and an `OCRCorrection` record containing original vs corrected JSON.

## Local OCR API

- `GET /health`
- `POST /ocr/analyze`
- `POST /ocr/region`
- `POST /preprocess`

Coordinates are normalized (`0..1`) so annotations are resolution-independent.

## Human-in-the-loop safety

OCR confidence is a model/process signal, not a legal guarantee. TaxFlow never auto-confirms an incoming invoice. The user must review required fields before `CONFIRMED` status is written.

## Current limitations

- Interactive rectangle selection currently works for JPG/PNG. PDF is displayed in the browser viewer and is OCRed by the backend/local service page-by-page, but direct PDF canvas annotations are not yet rendered in the React UI.
- Local OCR item-table extraction is conservative. Gemini can fill missing complex table fields in Hybrid mode.
- Model training is not faked. Verified `OCRCorrection` data is stored for future dataset export/training, but no automatic model fine-tuning job is claimed yet.
