# Mẫu Excel V20

`build_v20.py` tạo file `Theo_doi_san_luong_V20.xlsx` (cần Python 3 + openpyxl):

    python build_v20.py Theo_doi_san_luong_V20.xlsx

Bản 4.x dùng file này làm mẫu khi xuất Excel: `src/DisplayBoard.Core/Data/Templates/Theo_doi_san_luong_V20.xlsx`.
Sửa công thức thì sửa cả `DisplayCalculator` cho khớp; bài test `ExcelExportFormulaTests` so số app tính với số LibreOffice tính lại trên file xuất.
