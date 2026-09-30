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

files.forEach(f => {
  const content = fs.readFileSync(f, 'utf8');
  if (content.includes('reports-bulk') || content.includes('SaveReportsBulk') || content.includes('AddChildReport')) {
    const lines = content.split('\n');
    lines.forEach((line, idx) => {
      if (line.includes('reports-bulk') || line.includes('public async Task') || line.includes('AddChildReport') || line.includes('Metrics')) {
        console.log(`${path.relative(root, f)}:${idx + 1}: ${line.trim()}`);
      }
    });
  }
});
