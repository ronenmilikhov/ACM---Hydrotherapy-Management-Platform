import { StyleSheet, StatusBar, Platform } from 'react-native';

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#F2F6FA',
  },
  aquaticBackground: {
    ...StyleSheet.absoluteFillObject,
  },

  headerWaveBack: {
    position: 'absolute',
    top: 72,
    left: -12,
    right: -12,
    height: 75,
    borderRadius: 45,
    backgroundColor: 'rgba(131, 190, 232, 0.30)',
  },
  headerWaveFront: {
    position: 'absolute',
    top: 82,
    left: -22,
    right: -18,
    height: 65,
    borderRadius: 45,
    backgroundColor: 'rgba(149, 207, 244, 0.45)',
  },

  header: {
    paddingTop: (StatusBar.currentHeight || 0) + 10,
    paddingHorizontal: 16,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  iconButton: {
    width: 40,
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerIcon: {
    fontSize: 24,
    color: '#1789CD',
    lineHeight: 26,
    fontWeight: '700',
  },
  searchLabel: {
    color: '#517796',
    fontSize: 12,
    marginTop: 1,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  headerCenter: {
    alignItems: 'center',
  },
  headerTitle: {
    color: '#1E7FC0',
    fontSize: 24,
    fontWeight: '700',
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  headerSubtitle: {
    color: '#5E7B95',
    marginTop: 3,
    fontSize: 12,
    textAlign: 'center',
    writingDirection: 'rtl',
  },

  scrollContent: {
    paddingHorizontal: 22,
    paddingBottom: 34,
    paddingTop: 16,
  },
  mainCard: {
    backgroundColor: '#FFFFFF',
    borderRadius: 24,
    borderWidth: 1,
    borderColor: 'rgba(51, 110, 163, 0.12)',
    paddingHorizontal: 18,
    paddingTop: 16,
    paddingBottom: 18,
  },

  reportHeader: {
    marginBottom: 10,
  },
  reportChild: {
    color: '#1F5E9B',
    textAlign: 'right',
    fontSize: 18,
    writingDirection: 'rtl',
  },
  reportDate: {
    color: '#798C9D',
    textAlign: 'right',
    marginTop: 4,
    marginBottom: 8,
    fontSize: 14,
    writingDirection: 'rtl',
  },

  sectionTitle: {
    color: '#204A78',
    textAlign: 'right',
    fontSize: 28,
    marginTop: 6,
    marginBottom: 8,
    writingDirection: 'rtl',
  },

  progressItem: {
    marginBottom: 14,
  },
  progressLabelRow: {
    flexDirection: Platform.OS === 'ios' ? 'row-reverse' : 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 4,
  },
  progressLabel: {
    color: '#1E5086',
    textAlign: 'right',
    fontSize: 14,
    writingDirection: 'rtl',
  },
  progressPercent: {
    color: '#60768A',
    textAlign: 'right',
    fontSize: 13,
  },
  progressTrack: {
    height: 12,
    borderRadius: 8,
    backgroundColor: '#E8F0F8',
    overflow: 'hidden',
  },
  progressFill: {
    height: '100%',
    borderRadius: 8,
    backgroundColor: '#2E77BC',
  },

  notesBox: {
    marginTop: 6,
    marginBottom: 12,
    minHeight: 120,
    borderRadius: 14,
    backgroundColor: '#F7FAFD',
    paddingHorizontal: 14,
    paddingVertical: 12,
  },
  notesText: {
    color: '#607286',
    fontSize: 14,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  actionButtonShell: {
    borderRadius: 14,
    overflow: 'hidden',
    marginTop: 4,
    marginBottom: 10,
  },
  actionButton: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 12,
    borderRadius: 30,
  },
  actionButtonText: {
    color: '#EFF9FF',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
  },

  bottomLink: {
    marginTop: 8,
    color: '#365A7A',
    textAlign: 'center',
    fontSize: 14,
    writingDirection: 'rtl',
  },
});

export default styles;
