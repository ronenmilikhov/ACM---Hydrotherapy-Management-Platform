const fs = require('fs');
const path = require('path');

function walk(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    if (file === 'bin' || file === 'obj' || file === '.git' || file === 'node_modules') return;
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      results = results.concat(walk(fullPath));
    } else if (file.endsWith('.cs')) {
      results.push(fullPath);
    }
  });
  return results;
}

const root = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide');
const files = walk(root);

console.log(`Scanning ${files.length} cs files...`);
files.forEach(f => {
  const content = fs.readFileSync(f, 'utf8');
  if (content.toLowerCase().includes('אין נתונים') || content.toLowerCase().includes('מדדים') || content.toLowerCase().includes('חציון')) {
    const lines = content.split('\n');
    lines.forEach((line, idx) => {
      if (line.includes('אין נתונים') || line.includes('מדדים') || line.includes('חציון')) {
        console.log(`${path.relative(root, f)}:${idx + 1}: ${line.trim()}`);
      }
    });
  }
});
