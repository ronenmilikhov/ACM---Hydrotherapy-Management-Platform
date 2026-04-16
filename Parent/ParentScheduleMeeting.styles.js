import { StyleSheet, StatusBar } from 'react-native';

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
    top: 88,
    left: -12,
    right: -12,
    height: 95,
    borderRadius: 55,
    backgroundColor: 'rgba(131, 190, 232, 0.30)',
  },
  headerWaveFront: {
    position: 'absolute',
    top: 102,
    left: -22,
    right: -18,
    height: 82,
    borderRadius: 55,
    backgroundColor: 'rgba(149, 207, 244, 0.45)',
  },

  headerDate: {
    paddingTop: (StatusBar.currentHeight || 0) + 14,
    paddingHorizontal: 18,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  iconButton: {
    minWidth: 48,
    alignItems: 'center',
  },
  headerIcon: {
    fontSize: 32,
    color: '#1789CD',
    lineHeight: 34,
    fontWeight: '700',
  },
  searchLabel: {
    color: '#517796',
    fontSize: 15,
    marginTop: 2,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  headerCenterDate: {
    alignItems: 'center',
  },
  headerTitleDate: {
    color: '#1E7FC0',
    fontSize: 30,
    fontWeight: '700',
    textAlign: 'center',
    writingDirection: 'rtl',
  },

  scrollContent: {
    paddingHorizontal: 22,
    paddingBottom: 30,
    paddingTop: 20,
  },
  mainCard: {
    backgroundColor: '#FFFFFF',
    borderRadius: 24,
    borderWidth: 1,
    borderColor: 'rgba(51, 110, 163, 0.12)',
    paddingHorizontal: 18,
    paddingTop: 16,
    paddingBottom: 22,
    minHeight: 520,
  },

  weekdayRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 8,
  },
  weekdayText: {
    width: '14.285%',
    textAlign: 'center',
    color: '#246CA8',
    fontSize: 18,
    writingDirection: 'rtl',
  },
  calendarGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    rowGap: 10,
    marginBottom: 26,
  },
  dayOuter: {
    width: '14.285%',
    alignItems: 'center',
  },
  dayPlaceholder: {
    width: '14.285%',
    height: 56,
  },
  dayChip: {
    minWidth: 44,
    height: 36,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  dayChipSelected: {
    borderWidth: 2,
    borderColor: '#1887D4',
  },
  dayText: {
    color: '#0D5E9C',
    fontSize: 16,
    lineHeight: 20,
  },
  dayTextSelected: {
    color: '#0D5E9C',
    fontWeight: '700',
  },
  confirmButtonShell: {
    borderRadius: 14,
    overflow: 'hidden',
    alignSelf: 'center',
    width: '72%',
    marginTop: 'auto',
  },
  confirmButton: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 12,
    borderRadius: 30,
  },
  confirmButtonText: {
    color: '#EFF9FF',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
  },

  bigCircleLeft: {
    position: 'absolute',
    width: 160,
    height: 160,
    borderRadius: 80,
    left: -34,
    top: 56,
    backgroundColor: 'rgba(207, 232, 250, 0.9)',
  },
  bigCircleRight: {
    position: 'absolute',
    width: 210,
    height: 210,
    borderRadius: 105,
    right: -78,
    top: -18,
    backgroundColor: 'rgba(196, 224, 245, 0.6)',
  },

  headerTime: {
    paddingTop: (StatusBar.currentHeight || 0) + 14,
    paddingHorizontal: 18,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
  },
  backButton: {
    width: 52,
    height: 52,
    borderRadius: 26,
    alignItems: 'center',
    justifyContent: 'center',
  },
  backButtonIcon: {
    fontSize: 56,
    color: '#1A1A1A',
    lineHeight: 56,
  },
  timeHeaderCenter: {
    flex: 1,
    alignItems: 'center',
    paddingRight: 18,
  },
  timeHeaderTitle: {
    color: '#101010',
    fontSize: 28,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  timeHeaderSub: {
    marginTop: 6,
    color: '#365A7A',
    fontSize: 14,
    writingDirection: 'rtl',
  },

  scrollContentTime: {
    paddingHorizontal: 22,
    paddingBottom: 30,
    paddingTop: 18,
  },
  mainCardTime: {
    backgroundColor: '#FFFFFF',
    borderRadius: 24,
    borderWidth: 1,
    borderColor: 'rgba(51, 110, 163, 0.12)',
    paddingHorizontal: 18,
    paddingTop: 18,
    paddingBottom: 24,
    minHeight: 520,
    alignItems: 'center',
  },
  timeStepTitle: {
    color: '#313131',
    fontSize: 26,
    fontWeight: '700',
    writingDirection: 'rtl',
    alignSelf: 'flex-end',
    marginBottom: 6,
  },
  clockWrap: {
    marginTop: 8,
    marginBottom: 10,
  },

  selectedTimeBox: {
    marginTop: 8,
    width: 210,
    height: 56,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: '#A5D4F6',
    backgroundColor: '#EDF6FD',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
  },
  selectedTimeBig: {
    color: '#1575E8',
    fontSize: 28,
    fontWeight: '800',
  },
  chevron: {
    marginLeft: 8,
    color: '#101010',
    fontSize: 24,
    fontWeight: '700',
  },

  timeOptionsList: {
    marginTop: 8,
    width: 210,
    maxHeight: 210,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: '#A5D4F6',
    backgroundColor: '#FFFFFF',
    paddingVertical: 6,
  },
  timeOptionItem: {
    paddingVertical: 10,
    alignItems: 'center',
  },
  timeOptionItemSelected: {
    backgroundColor: '#E6F3FF',
  },
  timeOptionText: {
    color: '#2D79C3',
    fontSize: 16,
    fontWeight: '600',
  },
  timeOptionTextSelected: {
    color: '#1575E8',
    fontWeight: '800',
  },

  confirmTimeButtonShell: {
    borderRadius: 14,
    overflow: 'hidden',
    alignSelf: 'center',
    width: '88%',
    marginTop: 18,
  },
  confirmTimeButton: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 12,
    borderRadius: 30,
  },
  confirmTimeButtonText: {
    color: '#EFF9FF',
    fontSize: 16,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
});

export default styles;
