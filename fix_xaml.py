import io

with io.open(r'D:\QRCode\POSPrinter\Views\InvoicePage.xaml', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Remove orphan attributes at lines 480-481 (0-indexed)
# These are leftover from the deleted QR Label element
del lines[480:482]

with io.open(r'D:\QRCode\POSPrinter\Views\InvoicePage.xaml', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print(f"Done, total lines: {len(lines)}")
