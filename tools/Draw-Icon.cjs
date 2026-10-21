// Draw the "Signal Bridge" mark: an open receiver and a waveform routed outward.
// npm install --prefix tools/icon @napi-rs/canvas
let api;
try { api = require('./icon/node_modules/@napi-rs/canvas'); }
catch { api = require('../.tools/icon/node_modules/@napi-rs/canvas'); }
const { createCanvas, Path2D } = api;
const fs = require('node:fs');
const path = require('node:path');
const destination = path.resolve(__dirname, '../src/A2dpSinkRouter/Assets');
fs.mkdirSync(destination, { recursive: true });
const receiver = 'M153 53H93C67 53 48 72 48 98V158C48 184 67 203 93 203H153';
const signal = 'M79 137H103L122 99L147 157L166 119H209';
const arrow = 'M190 99L210 119L190 139';
function paint(c, size, nativeSize = size) {
  c.scale(size / 256, size / 256);
  c.fillStyle = '#1d1d1f'; c.beginPath(); c.roundRect(4, 4, 248, 248, 48); c.fill();
  // The open enclosure is the receiver; the bright line exits its right side.
  c.lineCap = 'round'; c.lineJoin = 'round';
  c.strokeStyle = '#858589'; c.lineWidth = 16;
  c.stroke(new Path2D(receiver));
  c.strokeStyle = '#fafafa'; c.lineWidth = nativeSize <= 24 ? 17 : 15;
  c.stroke(new Path2D(signal)); c.stroke(new Path2D(arrow));
}
function draw(size) {
  const canvas = createCanvas(size * 4, size * 4);
  paint(canvas.getContext('2d'), size * 4, size);
  const output = createCanvas(size, size);
  const o = output.getContext('2d');
  o.imageSmoothingEnabled = true; o.imageSmoothingQuality = 'high';
  o.drawImage(canvas, 0, 0, size, size);
  return output.toBuffer('image/png');
}
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const pngs = sizes.map(draw);
const header = Buffer.alloc(6 + 16 * sizes.length);
header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
sizes.forEach((size, index) => {
  const p = 6 + index * 16;
  header[p] = header[p + 1] = size === 256 ? 0 : size;
  header.writeUInt16LE(1, p + 4); header.writeUInt16LE(32, p + 6);
  header.writeUInt32LE(pngs[index].length, p + 8); header.writeUInt32LE(offset, p + 12);
  offset += pngs[index].length;
});
fs.writeFileSync(path.join(destination, 'app.ico'), Buffer.concat([header, ...pngs]));
fs.writeFileSync(path.join(destination, 'app.png'), draw(512));
fs.writeFileSync(path.join(destination, 'app.svg'), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect x="4" y="4" width="248" height="248" rx="48" fill="#1d1d1f"/><g fill="none" stroke-linecap="round" stroke-linejoin="round"><path d="${receiver}" stroke="#858589" stroke-width="16"/><path d="${signal}" stroke="#fafafa" stroke-width="15"/><path d="${arrow}" stroke="#fafafa" stroke-width="15"/></g></svg>\n`);
// Review sheet: native-size images are placed without upscaling.
const sheet = createCanvas(900, 420);
const c = sheet.getContext('2d');
c.fillStyle = '#f0f0f0'; c.fillRect(0, 0, 900, 420);
c.save(); c.translate(36, 36); paint(c, 320); c.restore();
c.fillStyle = '#222'; c.font = 'bold 28px sans-serif'; c.fillText('SIGNAL BRIDGE', 404, 78);
c.fillStyle = '#666'; c.font = '17px sans-serif'; c.fillText('A2DP Sink Router', 404, 110);
let x = 404;
for (const size of [16, 24, 32, 48, 64]) {
  c.save(); c.translate(x, 170); paint(c, size); c.restore();
  c.fillStyle = '#666'; c.font = '13px sans-serif'; c.fillText(String(size), x, 260);
  x += size + 27;
}
c.fillStyle = '#666'; c.font = '16px sans-serif'; c.fillText('Receive. Route. Play.', 404, 330);
const review = path.resolve(__dirname, '../artifacts/previews');
fs.mkdirSync(review, { recursive: true });
fs.writeFileSync(path.join(review, 'signal-bridge-icon.png'), sheet.toBuffer('image/png'));
console.log('Signal Bridge: multi-size ICO, 512 px PNG, SVG, and review sheet.');
