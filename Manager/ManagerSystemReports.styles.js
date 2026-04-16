import { StyleSheet } from 'react-native';

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#001529',
  },
  scrollContent: {
    paddingHorizontal: 12,
    paddingTop: 24,
    paddingBottom: 28,
  },
  errorText: {
    marginTop: 8,
    color: '#ff9b9b',
    fontSize: 14,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  stateCard: {
    marginBottom: 16,
    borderRadius: 14,
    borderWidth: 1,
    paddingHorizontal: 14,
    paddingVertical: 14,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
  },
  stateCardError: {
    borderColor: 'rgba(255, 128, 128, 0.48)',
    backgroundColor: 'rgba(255, 122, 122, 0.14)',
  },
  stateCardText: {
    color: '#ffd7d7',
    fontSize: 14,
    flex: 1,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  loadingWrap: {
    marginTop: 56,
    alignItems: 'center',
  },
  loadingText: {
    marginTop: 14,
    color: '#bdefff',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
    textAlign: 'center',
  },

  animatedContent: {
    width: '100%',
    paddingBottom: 40,
    paddingHorizontal: 4,
  },

  summaryCard: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.14)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    padding: 18,
    marginBottom: 16,
  },
  summaryHeaderRow: {
    flexDirection: 'row-reverse',
    alignItems: 'center',
    marginBottom: 10,
    gap: 8,
  },
  summaryTitle: {
    fontSize: 18,
    fontWeight: '700',
    color: '#fff2d2',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  summaryText: {
    fontSize: 14,
    color: 'rgba(255,255,255,0.84)',
    lineHeight: 22,
    writingDirection: 'rtl',
    textAlign: 'right',
  },

  groupCard: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.14)',
    backgroundColor: 'rgba(255,255,255,0.07)',
    padding: 16,
    marginBottom: 12,
  },
  groupHeader: {
    flexDirection: 'row-reverse',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 10,
  },
  groupTitle: {
    fontSize: 19,
    fontWeight: '800',
    color: '#ecfaff',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  groupAccent: {
    width: 4,
    alignSelf: 'stretch',
    borderRadius: 999,
    backgroundColor: 'rgba(0, 212, 255, 0.75)',
  },

  metricGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    marginBottom: 2,
  },
  metricCard: {
    width: '48%',
    borderRadius: 10,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.1)',
    backgroundColor: 'rgba(0,21,41,0.42)',
    paddingHorizontal: 10,
    paddingVertical: 9,
    marginBottom: 10,
    flexDirection: 'row-reverse',
    alignItems: 'center',
    gap: 8,
  },
  metricInfo: {
    flex: 1,
  },
  metricLabel: {
    fontSize: 12,
    color: 'rgba(255,255,255,0.74)',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  metricValue: {
    fontSize: 14,
    fontWeight: '800',
    color: '#d9f8ff',
    marginTop: 1,
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  metricsEmptyText: {
    color: 'rgba(255,255,255,0.62)',
    fontSize: 13,
    fontStyle: 'italic',
    marginBottom: 10,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  aiNotesWrap: {
    borderTopWidth: 1,
    borderTopColor: 'rgba(255,255,255,0.12)',
    paddingTop: 12,
    marginTop: 2,
  },
  aiNotesHeader: {
    flexDirection: 'row-reverse',
    alignItems: 'center',
    marginBottom: 6,
    gap: 6,
  },
  aiNotesTitle: {
    fontSize: 14,
    fontWeight: '700',
    color: '#ffe7a8',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  aiNotesText: {
    fontSize: 14,
    color: 'rgba(255,255,255,0.82)',
    lineHeight: 22,
    writingDirection: 'rtl',
    textAlign: 'right',
  },

  refreshButton: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(0,212,255,0.45)',
    backgroundColor: 'rgba(0,212,255,0.2)',
    flexDirection: 'row-reverse',
    justifyContent: 'center',
    alignItems: 'center',
    paddingVertical: 14,
    paddingHorizontal: 16,
    marginTop: 6,
    gap: 8,
  },
  refreshButtonText: {
    color: '#dff8ff',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  emptyStateText: {
    marginTop: 8,
    color: 'rgba(255,255,255,0.66)',
    fontSize: 14,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
});

export default styles;
